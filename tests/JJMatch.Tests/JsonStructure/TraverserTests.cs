using System.Text;
using System.Text.Json;

namespace JJMatch.Tests.JsonStructure;

// Every input is a section of TestData/ComplexJson, and the expected nodes are written out by hand from it.
public class TraverserTests
{
    private static readonly JsonElement Left = Load("Left.json");
    private static readonly JsonElement Right = Load("Right.json");

    private static JsonElement Load(string fileName)
    {
        using var document = JsonDocument.Parse(ReadText(fileName));
        return document.RootElement.Clone();
    }

    private static string ReadText(string fileName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ComplexJson", fileName)),
            new JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.GetProperty("core").GetRawText();
    }

    // Invalid UTF-8 cannot be written in the fixtures, which are text: the fixture's own bytes are parsed after the
    // last byte of each marked text is overwritten with 0xFF, a byte no UTF-8 text holds. Recorded in Docs/Decisions.md.
    private static JsonElement LoadWithInvalidUtf8(string fileName, params string[] markers)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "ComplexJson", fileName));
        foreach (var marker in markers)
        {
            var text = Encoding.UTF8.GetBytes(marker);
            var start = bytes.AsSpan().IndexOf(text);
            Assert.True(start >= 0 && start == bytes.AsSpan().LastIndexOf(text), $"'{marker}' must occur exactly once.");
            bytes[start + text.Length - 1] = 0xFF;
        }

        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.GetProperty("core").Clone();
    }

    private static JsonStructureNode Tree(JsonElement element) =>
        global::JJMatch.JsonStructureTraverser.Traverse(element);

    // The name, kind and value of each child, in the order the tree holds them.
    private static IEnumerable<(string? Name, JsonValueKind Kind, string? Value)> Children(JsonStructureNode node) =>
        node.Children.Select(child => (child.PropertyName, child.ValueKind, child.Value));

    private static JsonStructureNode Child(JsonStructureNode node, string name) =>
        node.Children.Single(child => child.PropertyName == name);

    // The root node has no name, whatever its kind; a primitive root is a single node without children.
    [Fact]
    public void Tree_root_is_one_unnamed_node_for_an_object_an_array_or_a_primitive()
    {
        var left = Tree(Left);
        var right = Tree(Right);

        Assert.Null(left.PropertyName);
        Assert.Equal(JsonValueKind.Object, left.ValueKind);
        Assert.Null(left.Value);

        // The first top-level properties, in document order. Right has no "deletedAt", and its "version" is a string.
        Assert.Equal(
            [
                ("id", JsonValueKind.String, "ord-2024-000187"),
                ("version", JsonValueKind.Number, "3"),
                ("active", JsonValueKind.True, null),
                ("deletedAt", JsonValueKind.Null, null),
                ("customer", JsonValueKind.Object, null),
            ],
            Children(left).Take(5));

        Assert.Equal(
            [
                ("id", JsonValueKind.String, "ord-2024-000188"),
                ("version", JsonValueKind.String, "3"),
                ("active", JsonValueKind.False, null),
                ("customer", JsonValueKind.Object, null),
            ],
            Children(right).Take(4));

        // A root array holds its elements as unnamed children.
        var items = Tree(Left.GetProperty("items"));

        Assert.Null(items.PropertyName);
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Null(items.Value);
        Assert.Equal([(null, JsonValueKind.Object, null), (null, JsonValueKind.Object, null)], Children(items));

        // A root primitive: the kind tells true from false and a number from the string spelling the same digits.
        var primitives = new (JsonElement Element, JsonValueKind Kind, string? Value)[]
        {
            (Left.GetProperty("id"), JsonValueKind.String, "ord-2024-000187"),
            (Left.GetProperty("version"), JsonValueKind.Number, "3"),
            (Right.GetProperty("version"), JsonValueKind.String, "3"),
            (Left.GetProperty("customer").GetProperty("größe"), JsonValueKind.Number, "172.5"),
            (Left.GetProperty("active"), JsonValueKind.True, null),
            (Right.GetProperty("active"), JsonValueKind.False, null),
            (Left.GetProperty("deletedAt"), JsonValueKind.Null, null),
        };

        Assert.All(primitives, primitive =>
        {
            var node = Tree(primitive.Element);

            Assert.Null(node.PropertyName);
            Assert.Equal(primitive.Kind, node.ValueKind);
            Assert.Equal(primitive.Value, node.Value);
            Assert.Empty(node.Children);
        });
    }

    // "customer" holds strings, numbers, an object and arrays under plain, spaced and non-ASCII names.
    [Fact]
    public void Tree_object_keeps_every_property_in_document_order_with_its_kind_and_value()
    {
        var left = Tree(Left.GetProperty("customer"));
        var right = Tree(Right.GetProperty("customer"));

        Assert.Equal(
            [
                ("id", JsonValueKind.Number, "48213"),
                ("name", JsonValueKind.String, "Zoë Åkesson"),
                ("e-mail", JsonValueKind.String, "zoe@example.com"),
                ("first name", JsonValueKind.String, "Zoë"),
                ("名前", JsonValueKind.String, "ゾーイ"),
                ("größe", JsonValueKind.Number, "172.5"),
                ("preferences", JsonValueKind.Object, null),
                ("tags", JsonValueKind.Array, null),
                ("addresses", JsonValueKind.Array, null),
            ],
            Children(left));

        // Same names in the same order; "größe" is a string here, and the value says so.
        Assert.Equal(
            [
                ("id", JsonValueKind.Number, "48213"),
                ("name", JsonValueKind.String, "Zoë Åkesson"),
                ("e-mail", JsonValueKind.String, "zoe@example.org"),
                ("first name", JsonValueKind.String, "Zoë"),
                ("名前", JsonValueKind.String, "ゾーイ"),
                ("größe", JsonValueKind.String, "172.5 cm"),
                ("preferences", JsonValueKind.Object, null),
                ("tags", JsonValueKind.Array, null),
                ("addresses", JsonValueKind.Array, null),
            ],
            Children(right));

        // Property order is kept as written, not sorted: the two fixtures reverse "propertyOrder" at every level.
        Assert.Equal(["head", "foot"], Tree(Left.GetProperty("propertyOrder")).Children.Select(child => child.PropertyName));
        Assert.Equal(["foot", "head"], Tree(Right.GetProperty("propertyOrder")).Children.Select(child => child.PropertyName));

        // Nested containers hang below the property that holds them.
        Assert.Equal(
            [("lat", JsonValueKind.Number, "57.7089"), ("lng", JsonValueKind.Number, "11.9746")],
            Children(Child(Child(left, "addresses").Children[0], "geo")));

        Assert.Equal(
            [(null, JsonValueKind.Number, "55.605"), (null, JsonValueKind.Number, "13.0038")],
            Children(Child(Child(right, "addresses").Children[0], "geo")));
    }

    // "" is a real property name in both fixtures; only the root and array elements have no name.
    [Fact]
    public void Tree_keeps_an_empty_property_name_distinct_from_no_name_and_decodes_escaped_names()
    {
        foreach (var root in new[] { Left, Right })
        {
            var tree = Tree(root);
            var emptyNamed = Child(tree, "");

            Assert.NotNull(emptyNamed.PropertyName);
            Assert.Equal(JsonValueKind.Object, emptyNamed.ValueKind);
            Assert.Equal([("name", JsonValueKind.String, "empty property name")], Children(emptyNamed));

            // Escapes in names are decoded, as they are in the dictionary keys.
            var escaped = Child(tree, "escapedNames");

            Assert.Equal([("quote\"d", JsonValueKind.Number, "1"), ("back\\slash", JsonValueKind.Object, null)], Children(escaped));
            Assert.Equal([("quote\"d", JsonValueKind.Number, "2"), ("café", JsonValueKind.Number, "3")], Children(escaped.Children[1]));
        }

        Assert.Equal(
            [
                ("depth", JsonValueKind.Number, "3"),
                ("name", JsonValueKind.String, "innermost"),
                ("", JsonValueKind.String, "nested empty property name"),
            ],
            Children(Tree(Left.GetProperty("meta").GetProperty("meta").GetProperty("meta"))));

        // Array elements are unnamed, whatever they are.
        Assert.All(Tree(Left.GetProperty("mixed")).Children, element => Assert.Null(element.PropertyName));
    }

    // "duplicateKeys" declares "name" twice in one object, with different values, in both fixtures.
    [Fact]
    public void Tree_keeps_duplicate_property_names_as_separate_children_with_their_own_values()
    {
        foreach (var root in new[] { Left, Right })
        {
            Assert.Equal(
                [
                    ("name", JsonValueKind.String, "Ali"),
                    ("name", JsonValueKind.String, "Reza"),
                    ("age", JsonValueKind.Number, "30"),
                ],
                Children(Tree(root.GetProperty("duplicateKeys"))));
        }
    }

    // Left has "comment" (""), "notes" and "deletedAt" (null), "preferences" ({}) and "tags" ([]).
    // Right has no "comment" or "deletedAt", and non-empty "notes", "preferences" and "tags".
    [Fact]
    public void Tree_tells_empty_values_apart_and_has_no_node_for_a_missing_property()
    {
        var left = Tree(Left);
        var right = Tree(Right);

        // An empty string has the value "", null has no value, and the kind separates the two empty containers.
        var comment = Child(left, "comment");
        var notes = Child(left, "notes");
        var preferences = Child(Child(left, "customer"), "preferences");
        var tags = Child(Child(left, "customer"), "tags");

        Assert.Equal((JsonValueKind.String, ""), (comment.ValueKind, comment.Value));
        Assert.Equal((JsonValueKind.Null, null), (notes.ValueKind, notes.Value));
        Assert.Equal((JsonValueKind.Object, null), (preferences.ValueKind, preferences.Value));
        Assert.Equal((JsonValueKind.Array, null), (tags.ValueKind, tags.Value));

        Assert.All(new[] { comment, notes, preferences, tags }, node => Assert.Empty(node.Children));

        // Where Right's values are not empty, the same properties carry a value or children.
        Assert.Equal((JsonValueKind.String, "leave at the door"), (Child(right, "notes").ValueKind, Child(right, "notes").Value));
        Assert.Equal([("newsletter", JsonValueKind.True, null)], Children(Child(Child(right, "customer"), "preferences")));
        Assert.Equal([(null, JsonValueKind.String, "vip")], Children(Child(Child(right, "customer"), "tags")));

        // A missing property has no node at all, which is different from a node of kind Null.
        Assert.Equal(JsonValueKind.Null, Child(left, "deletedAt").ValueKind);
        Assert.DoesNotContain(right.Children, child => child.PropertyName is "comment" or "deletedAt");
        Assert.DoesNotContain(left.Children, child => child.PropertyName == "shipping");
    }

    // "matrix" nests arrays of numbers, and "arrayBoundaries" groups the same two objects differently in each fixture:
    // [ [ A ], [ B ] ] in Left and [ [ A, B ], [] ] in Right. The dictionary cannot tell these apart; the tree can.
    [Fact]
    public void Tree_keeps_array_element_order_and_nested_array_boundaries()
    {
        var leftMatrix = Tree(Left.GetProperty("matrix"));
        var rightMatrix = Tree(Right.GetProperty("matrix"));

        Assert.Equal([3, 3, 0], leftMatrix.Children.Select(row => row.Children.Count));
        Assert.Equal([4, 5, 6], leftMatrix.Children[1].Children.Select(cell => int.Parse(cell.Value!)));
        Assert.All(leftMatrix.Children, row => Assert.Equal((null, JsonValueKind.Array), (row.PropertyName, row.ValueKind)));

        Assert.Equal([3, 2, 0, 2], rightMatrix.Children.Select(row => row.Children.Count));
        Assert.Equal([(null, JsonValueKind.Number, "7"), (null, JsonValueKind.Array, null)], Children(rightMatrix.Children[3]));
        Assert.Equal(
            [(null, JsonValueKind.Number, "8"), (null, JsonValueKind.Number, "9")],
            Children(rightMatrix.Children[3].Children[1]));

        var leftBoundaries = Tree(Left.GetProperty("arrayBoundaries"));
        var rightBoundaries = Tree(Right.GetProperty("arrayBoundaries"));

        // The same two objects grouped differently: each inner array keeps its own elements.
        Assert.Equal([1, 1], leftBoundaries.Children.Select(inner => inner.Children.Count));
        Assert.Equal([2, 0], rightBoundaries.Children.Select(inner => inner.Children.Count));

        Assert.Equal([("name", JsonValueKind.Number, "1")], Children(leftBoundaries.Children[0].Children[0]));
        Assert.Equal(
            [("name", JsonValueKind.Number, "2"), ("age", JsonValueKind.Number, "3")],
            Children(leftBoundaries.Children[1].Children[0]));

        Assert.Equal([("name", JsonValueKind.Number, "1")], Children(rightBoundaries.Children[0].Children[0]));
        Assert.Equal(
            [("name", JsonValueKind.Number, "2"), ("age", JsonValueKind.Number, "3")],
            Children(rightBoundaries.Children[0].Children[1]));

        // Swapped elements stay swapped: "sameIds" extracts identically on both sides, but "gamma" sits in the
        // second element in Left and in the first in Right.
        static IEnumerable<string> ElementShapes(JsonStructureNode array) =>
            array.Children.Select(element => string.Join(",", element.Children.Select(property => property.PropertyName)));

        Assert.Equal(
            ["alpha,beta", "beta,alpha,gamma"],
            ElementShapes(Tree(Left.GetProperty("arrayElementOrder").GetProperty("sameIds"))));

        Assert.Equal(
            ["alpha,beta,gamma", "beta,alpha"],
            ElementShapes(Tree(Right.GetProperty("arrayElementOrder").GetProperty("sameIds"))));
    }

    // "history" mixes strings, numbers, booleans, null, an object, an array holding an object, and an empty array.
    [Fact]
    public void Tree_mixed_array_keeps_every_element_in_place_with_its_own_kind()
    {
        var left = Tree(Left.GetProperty("history"));
        var right = Tree(Right.GetProperty("history"));

        Assert.Equal(
            [
                (null, JsonValueKind.String, "created"),
                (null, JsonValueKind.Number, "42"),
                (null, JsonValueKind.True, null),
                (null, JsonValueKind.Null, null),
                (null, JsonValueKind.Object, null),
                (null, JsonValueKind.Array, null),
                (null, JsonValueKind.Array, null),
            ],
            Children(left));

        // Right writes 42 as a string, has no empty array, and ends with null.
        Assert.Equal(
            [
                (null, JsonValueKind.String, "created"),
                (null, JsonValueKind.String, "42"),
                (null, JsonValueKind.True, null),
                (null, JsonValueKind.Object, null),
                (null, JsonValueKind.Array, null),
                (null, JsonValueKind.Null, null),
            ],
            Children(right));

        // The object element keeps its properties, and the array element keeps its own mixed content.
        Assert.Equal(
            [
                ("event", JsonValueKind.String, "paid"),
                ("at", JsonValueKind.String, "2024-03-01T10:15:00Z"),
                ("meta", JsonValueKind.Object, null),
            ],
            Children(left.Children[4]));

        Assert.Equal(
            [
                (null, JsonValueKind.String, "retry"),
                (null, JsonValueKind.Number, "2"),
                (null, JsonValueKind.Object, null),
            ],
            Children(left.Children[5]));

        Assert.Equal(
            [("event", JsonValueKind.String, "webhook"), ("meta", JsonValueKind.Object, null)],
            Children(left.Children[5].Children[2]));

        Assert.Equal([("status", JsonValueKind.Number, "200")], Children(Child(left.Children[5].Children[2], "meta")));
        Assert.Empty(left.Children[6].Children);

        // "primitives" holds one value of every primitive kind.
        Assert.Equal(
            [
                (null, JsonValueKind.String, "text"),
                (null, JsonValueKind.Number, "42"),
                (null, JsonValueKind.Number, "3.5"),
                (null, JsonValueKind.True, null),
                (null, JsonValueKind.False, null),
                (null, JsonValueKind.Null, null),
            ],
            Children(Tree(Left.GetProperty("primitives"))));
    }

    // "primitiveValues" holds a 30-digit integer, a number in scientific notation in Left and written out in Right,
    // 1.0 in Left and 1 in Right, and one string written with escapes in Left and literally in Right.
    [Fact]
    public void Tree_keeps_numbers_as_their_raw_text_and_strings_decoded()
    {
        var left = Tree(Left.GetProperty("primitiveValues")).Children;
        var right = Tree(Right.GetProperty("primitiveValues")).Children;

        Assert.Equal(
            [JsonValueKind.Number, JsonValueKind.Number, JsonValueKind.Number, JsonValueKind.String],
            left.Select(node => node.ValueKind));

        Assert.Equal(left.Select(node => node.ValueKind), right.Select(node => node.ValueKind));

        // Every digit of a large integer survives: the two differ only in the last one, which a double cannot hold.
        Assert.Equal("123456789012345678901234567890", left[0].Value);
        Assert.Equal("123456789012345678901234567891", right[0].Value);
        Assert.Equal(
            double.Parse(left[0].Value!, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(right[0].Value!, System.Globalization.CultureInfo.InvariantCulture));

        // Numbers are not normalized: equal quantities written differently keep their own spelling.
        Assert.Equal("1.5E+10", left[1].Value);
        Assert.Equal("15000000000", right[1].Value);
        Assert.Equal("1.0", left[2].Value);
        Assert.Equal("1", right[2].Value);

        // Strings are decoded: \t, \", \\, \u00e9 and the surrogate pair \uD83D\uDE80 give the same text as the literal spelling.
        const string decoded = "tab\there \"quoted\" back\\slash caf\u00e9 \U0001F680";

        Assert.Equal(decoded, left[3].Value);
        Assert.Equal(decoded, right[3].Value);
    }

    // Left has "comment" (""), "preferences" ({}) and "tags" ([]): a primitive, an empty object and an empty array.
    [Fact]
    public void Tree_primitives_and_empty_containers_share_one_empty_child_list()
    {
        var left = Tree(Left);
        var comment = Child(left, "comment");
        var preferences = Child(Child(left, "customer"), "preferences");
        var tags = Child(Child(left, "customer"), "tags");

        Assert.Same(comment.Children, preferences.Children);
        Assert.Same(comment.Children, tags.Children);
    }

    // default(JsonElement) is not a JSON value: its kind is Undefined. It is walked like a primitive, without throwing.
    [Fact]
    public void Tree_default_element_is_one_undefined_node()
    {
        var node = Tree(default);

        Assert.Null(node.PropertyName);
        Assert.Equal(JsonValueKind.Undefined, node.ValueKind);
        Assert.Null(node.Value);
        Assert.Empty(node.Children);
    }

    // The parser accepts a lone surrogate escape (\uD800, \uDC00) in a name or a value but its own decoding rejects
    // it. Traverse decodes such a string itself, as the UTF-16 unit the escape names, so nothing is lost or changed:
    // a lone high surrogate, a lone low one and a valid pair are three different names, and the same in values.
    [Fact]
    public void Tree_keeps_lone_surrogate_escapes_in_names_and_values()
    {
        foreach (var root in new[] { Left, Right })
        {
            var section = Tree(root.GetProperty("loneSurrogates"));

            // The lone high surrogate name is declared twice; both occurrences are kept with their own values.
            Assert.Equal(
                [
                    ("\uD800", JsonValueKind.String, "high"),
                    ("\uDC00", JsonValueKind.String, "low"),
                    ("\U0001F600", JsonValueKind.String, "pair"),
                    ("\uD800", JsonValueKind.String, "high again"),
                    ("nested", JsonValueKind.Object, null),
                    ("values", JsonValueKind.Array, null),
                ],
                Children(section));

            // Nested: an array under a lone low surrogate name holds an object whose name and value are lone surrogates.
            var nested = Child(Child(section, "nested"), "\uDC00");
            Assert.Equal(JsonValueKind.Array, nested.ValueKind);
            Assert.Equal([("\uD800", JsonValueKind.String, "\uDC00")], Children(nested.Children[0]));

            // Values: each lone surrogate is one UTF-16 unit, the pair two, and a lone one inside text stays in place.
            var values = Child(section, "values").Children;
            Assert.Equal(["\uD800", "\uDC00", "\U0001F600", "x\uD800y"], values.Select(child => child.Value));
            Assert.Equal([1, 1, 2, 3], values.Select(child => child.Value!.Length));
        }
    }

    // Names that take the fallback together with everything else a name can hold: a reversed pair (declared twice,
    // around other names), a high surrogate before a letter, literal and escaped characters side by side, an escaped
    // quote, backslash and solidus, an escaped backslash before a literal "uD800", and the short escapes. A nested
    // object comes first, so its names must not be counted among the section's own.
    [Fact]
    public void Tree_decodes_lone_surrogate_names_among_other_escapes_duplicates_and_nesting()
    {
        foreach (var root in new[] { Left, Right })
        {
            var section = Tree(root.GetProperty("loneSurrogateNames"));

            Assert.Equal(
                [
                    ("plainFirst", JsonValueKind.Object, null),
                    ("\uDE00\uD83D", JsonValueKind.String, "reversed pair"),
                    ("\uD83Dx", JsonValueKind.Array, null),
                    ("\u00e9\u00e9\uD800\U0001F600\U0001F600", JsonValueKind.String, "literal and escaped"),
                    ("\uD800\"\\/", JsonValueKind.String, "quote, backslash, solidus"),
                    ("\\uD800\uDC00", JsonValueKind.String, "escaped backslash before u"),
                    ("\uDC00\b\f\n\r\t", JsonValueKind.String, "short escapes"),
                    ("\uDE00\uD83D", JsonValueKind.String, "reversed pair again"),
                    ("escapedValue", JsonValueKind.String, "\u00e9\u00e9\uD800\"\\/\b\f\n\r\t"),
                ],
                Children(section));

            // Every UTF-16 unit is kept: no surrogate is dropped, replaced or joined with a neighbour.
            Assert.Equal([10, 2, 2, 7, 4, 7, 6, 2, 12], section.Children.Select(child => child.PropertyName!.Length));

            // Nested: each object reads its own names, under an object and under an array.
            Assert.Equal(
                [("\uDBFF", JsonValueKind.Number, "1"), ("plainInner", JsonValueKind.Number, "2")],
                Children(section.Children[0]));
            Assert.Equal([("\uDFFFy", JsonValueKind.String, "under an array")], Children(section.Children[2].Children[0]));
        }
    }

    // A string value with invalid UTF-8 bytes has no Value and does not stop the walk, also where the names around
    // it take the fallback: directly under a lone surrogate name declared twice, below an array under such a name,
    // and in a value that holds a lone surrogate escape as well. Every name is still decoded, in order.
    [Fact]
    public void Invalid_utf8_string_values_do_not_stop_the_walk_of_objects_with_lone_surrogate_names()
    {
        foreach (var fileName in new[] { "Left.json", "Right.json" })
        {
            var root = LoadWithInvalidUtf8(fileName, "reversed pair again", "under an array", "\"escapedValue\": \"\u00e9");
            var section = Tree(root.GetProperty("loneSurrogateNames"));

            Assert.Equal(
                [
                    ("plainFirst", JsonValueKind.Object, null),
                    ("\uDE00\uD83D", JsonValueKind.String, "reversed pair"),
                    ("\uD83Dx", JsonValueKind.Array, null),
                    ("\u00e9\u00e9\uD800\U0001F600\U0001F600", JsonValueKind.String, "literal and escaped"),
                    ("\uD800\"\\/", JsonValueKind.String, "quote, backslash, solidus"),
                    ("\\uD800\uDC00", JsonValueKind.String, "escaped backslash before u"),
                    ("\uDC00\b\f\n\r\t", JsonValueKind.String, "short escapes"),
                    ("\uDE00\uD83D", JsonValueKind.String, null),
                    ("escapedValue", JsonValueKind.String, null),
                ],
                Children(section));
            Assert.Equal(
                [("\uDBFF", JsonValueKind.Number, "1"), ("plainInner", JsonValueKind.Number, "2")],
                Children(section.Children[0]));
            Assert.Equal([("\uDFFFy", JsonValueKind.String, null)], Children(section.Children[2].Children[0]));
        }
    }

    // A property name with invalid UTF-8 bytes cannot be represented and is not left out: Traverse throws, whether
    // or not the object also holds a lone surrogate name.
    [Fact]
    public void Invalid_utf8_property_name_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Tree(LoadWithInvalidUtf8("Left.json", "plainInner")));
        Assert.Throws<InvalidOperationException>(() => Tree(LoadWithInvalidUtf8("Left.json", "deletedAt")));
    }

    // A property whose decoded name starts with the prefix is left out with its subtree, at every level; the prefix
    // is matched as written, so "_" drops the name that is exactly "_" and "1" reaches the "1st" nested under
    // another name. Without a prefix every property stays.
    [Fact]
    public void Tree_leaves_out_properties_under_the_ignored_prefix_at_every_level()
    {
        var section = Left.GetProperty("pathEscapes");
        var all = Tree(section);
        Assert.Contains("_", all.Children.Select(child => child.PropertyName));
        Assert.Equal(new[] { "1st" }, Child(all, "line\u2028sep").Children.Select(child => child.PropertyName));

        var withoutUnderscore = global::JJMatch.JsonStructureTraverser.Traverse(section, ignoredPrefix: "_");
        Assert.Equal(all.Children.Count - 1, withoutUnderscore.Children.Count);
        Assert.Equal(all.Children.Where(child => child.PropertyName != "_").Select(child => child.PropertyName), withoutUnderscore.Children.Select(child => child.PropertyName));

        var withoutDigitOne = global::JJMatch.JsonStructureTraverser.Traverse(section, ignoredPrefix: "1");
        Assert.Equal(all.Children.Count, withoutDigitOne.Children.Count);
        Assert.Empty(Child(withoutDigitOne, "line\u2028sep").Children);
        Assert.Equal(JsonValueKind.Object, Child(withoutDigitOne, "line\u2028sep").ValueKind);
    }
}
