using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace JJMatch;

/// <summary>Walks a JSON element once and copies its structure and primitive values into a <see cref="JsonStructureNode"/> tree.</summary>
internal static class JsonStructureTraverser
{
    // Object properties and array elements become children in document order: nothing is sorted, merged or dropped.
    // Strings are stored decoded and numbers as their raw text; true, false and null are carried by the kind alone.
    // Empty objects and arrays share the empty child list of the primitives.
    // A positive stringifiedDepth also decodes a string holding a serialized object or array, at any level: the
    // string's node becomes the node of the document it holds, nested at most that many levels deep, which is
    // walked by the same rules. Every other string, and every string when it is 0, stays a string.
    internal static JsonStructureNode Traverse(JsonElement element, string? propertyName = null, ComparisonBudget? budget = null, int stringifiedDepth = 0)
    {
        budget?.VisitNode();
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                List<JsonStructureNode>? properties = null;
                List<string>? rawNames = null;
                var index = 0;
                foreach (var property in element.EnumerateObject())
                {
                    (properties ??= new List<JsonStructureNode>()).Add(Traverse(property.Value, DecodeName(property, element, index, ref rawNames), budget, stringifiedDepth - 1));
                    index++;
                }
                return new JsonStructureNode(propertyName, JsonValueKind.Object, null, Children(properties));

            case JsonValueKind.Array:
                List<JsonStructureNode>? items = null;
                foreach (var item in element.EnumerateArray())
                {
                    (items ??= new List<JsonStructureNode>(element.GetArrayLength())).Add(Traverse(item, budget: budget, stringifiedDepth: stringifiedDepth - 1));
                }
                return new JsonStructureNode(propertyName, JsonValueKind.Array, null, Children(items));

            case JsonValueKind.String:
                if (stringifiedDepth > 0 && Stringified(element, stringifiedDepth, budget) is { } document)
                {
                    using (document)
                    {
                        return Traverse(document.RootElement, propertyName, budget, stringifiedDepth);
                    }
                }
                return new JsonStructureNode(propertyName, JsonValueKind.String, DecodeString(element), Array.Empty<JsonStructureNode>());

            case JsonValueKind.Number:
                return new JsonStructureNode(propertyName, JsonValueKind.Number, element.GetRawText(), Array.Empty<JsonStructureNode>());

            default:
                return new JsonStructureNode(propertyName, element.ValueKind, null, Array.Empty<JsonStructureNode>());
        }
    }

    private static IReadOnlyList<JsonStructureNode> Children(List<JsonStructureNode>? children) =>
        children is null ? Array.Empty<JsonStructureNode>() : children;

    // The document a string holds when its text, after leading JSON white space, starts with '{' or '[' and parses
    // within maxDepth levels; otherwise null. A string that cannot be decoded as Unicode text holds none.
    internal static JsonDocument? Stringified(JsonElement element, int maxDepth, ComparisonBudget? budget)
    {
        string text;
        try
        {
            text = element.GetString()!;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        var start = text.AsSpan().TrimStart(" \t\r\n".AsSpan());
        if (start.Length == 0 || (start[0] != '{' && start[0] != '[')) return null;
        budget?.Spend();
        try
        {
            return JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = maxDepth });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The parser accepts a lone surrogate escape (\uD800, \uDC00) in a name, but JsonProperty.Name rejects it when
    // it decodes the name. Such a name is decoded from the object's raw text instead, keeping the surrogate as the
    // UTF-16 unit it names, so that no name is lost or changed. Names that decode normally take the normal path.
    private static string DecodeName(JsonProperty property, JsonElement parent, int index, ref List<string>? rawNames)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            rawNames ??= RawPropertyNames(parent);
            return Unescape(rawNames[index]);
        }
    }

    // The raw, still escaped, text of each immediate property name of an object, in document order. The object's
    // own UTF-8 text is read again, so this costs only when a name could not be decoded. It is reached through the
    // serializer, the only public way to a reader over an element's bytes: GetRawText would transcode the whole
    // object and throw for invalid UTF-8 in any string below it, although only the names are needed.
    private static List<string> RawPropertyNames(JsonElement parent)
    {
        try
        {
            return JsonSerializer.Deserialize(parent, RawNamesConverter.Info)!;
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException("A property name holds invalid UTF-8 bytes.", exception);
        }
    }

    private sealed class RawNamesConverter : JsonConverter<List<string>>
    {
        // The depth limit is lifted because the parser already enforced the caller's limit on the whole document.
        internal static readonly JsonTypeInfo<List<string>> Info = JsonMetadataServices.CreateValueInfo<List<string>>(
            new JsonSerializerOptions
            {
                MaxDepth = int.MaxValue,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                TypeInfoResolver = JsonTypeInfoResolver.Combine(),
            },
            new RawNamesConverter());

        // Invalid UTF-8 in a name is rejected, not replaced: such a name cannot be represented.
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var names = new List<string>();
            var depth = reader.CurrentDepth;
            while (reader.Read() && reader.CurrentDepth > depth)
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == depth + 1)
                {
                    names.Add(StrictUtf8.GetString(reader.ValueSpan.ToArray()));
                }
            }
            return names;
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    // A string the parser accepted but GetString rejects: a lone surrogate escape, decoded like a name above, or
    // invalid UTF-8 bytes, which cannot be represented and leave the node without a Value. Raw JSON text is never
    // stored as a Value.
    private static string? DecodeString(JsonElement element)
    {
        try
        {
            return element.GetString();
        }
        catch (InvalidOperationException)
        {
            try
            {
                var raw = element.GetRawText();
                return Unescape(raw.Substring(1, raw.Length - 2));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    // Decodes the escapes of a JSON string the parser has already validated. A \u escape becomes the UTF-16 unit it
    // names, whether or not it forms a pair with its neighbour; everything else is kept as written.
    private static string Unescape(string raw)
    {
        var text = new StringBuilder(raw.Length);
        for (var index = 0; index < raw.Length; index++)
        {
            var current = raw[index];
            if (current != '\\')
            {
                text.Append(current);
                continue;
            }

            current = raw[++index];
            switch (current)
            {
                case 'b':
                    text.Append('\b');
                    break;
                case 'f':
                    text.Append('\f');
                    break;
                case 'n':
                    text.Append('\n');
                    break;
                case 'r':
                    text.Append('\r');
                    break;
                case 't':
                    text.Append('\t');
                    break;
                case 'u':
                    text.Append((char)int.Parse(raw.Substring(index + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += 4;
                    break;
                default:
                    // A quote, a backslash or a solidus.
                    text.Append(current);
                    break;
            }
        }
        return text.ToString();
    }
}
