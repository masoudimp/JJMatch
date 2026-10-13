using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace JJMatch.Benchmarks;

// Run with: dotnet run -c Release --project benchmarks/JJMatch.Benchmarks -- --filter "*JJMatcher*"
// The public pipeline end to end: parsing, both trees, counting, matching, locating differences and snapshots.
// Synthetic documents are generated here, as the fixture rule covers tests only; the fixtures are compared too.
[MemoryDiagnoser]
public class JJMatcherBenchmarks
{
    private string _leftCore = "";
    private string _rightCore = "";
    private string _nested = "";
    private string _nestedReordered = "";
    private string _elements = "";
    private string _elementsPermuted = "";
    private string _shapes = "";
    private string _shapesOther = "";
    private string _duplicates = "";
    private string _duplicatesOther = "";
    private string _names = "";
    private string _namesOther = "";
    private string _wide = "";
    private string _wideOther = "";

    private static readonly JJMatchSettings Strict = new();
    private static readonly JJMatchSettings StructureOnly = new() { ArrayMode = JJArrayMatchingMode.StructureOnly };
    // The 200 names exceed the default group size of 128, and their 40,000 fuzzy candidates the default work budget.
    private static readonly JJMatchSettings Fuzzy = new()
    {
        ValueMode = JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric,
        MaxAssignmentSize = 256,
        MaxWork = 1_000_000_000,
    };

    [GlobalSetup]
    public void Setup()
    {
        _leftCore = Fixture("Left.json");
        _rightCore = Fixture("Right.json");
        // About 6,000 nodes, five properties per object and four elements per array, nested seven levels deep;
        // the second document reorders every object's properties and changes one number in a hundred.
        _nested = Nested(new Random(1), 0);
        _nestedReordered = Reorder(_nested, new Random(2), 0.01);
        _elements = Elements(120, index => index);
        _elementsPermuted = Elements(120, index => (index * 37) % 120);
        _shapes = Shapes(100, "left");
        _shapesOther = Shapes(100, "right");
        _duplicates = Duplicates(100, 0);
        _duplicatesOther = Duplicates(100, 50);
        _names = Names(200, index => $"Customer Name Number {index} Street");
        _namesOther = Names(200, index => $"customer name number {(index * 7) % 200} street ");
        _wide = Wide(20_000, index => index);
        _wideOther = Wide(20_000, index => index % 100 == 0 ? index + 1 : index);
    }

    private static string Fixture(string name)
    {
        using var stream = typeof(JJMatcherBenchmarks).Assembly.GetManifestResourceStream(name)!;
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.GetProperty("core").GetRawText();
    }

    private static string Nested(Random random, int depth)
    {
        if (depth >= 7) return Primitive(random);
        var container = depth < 3 ? random.Next(2) : random.Next(3);
        if (container == 2) return Primitive(random);
        var text = new StringBuilder(container == 0 ? "{" : "[");
        var count = container == 0 ? 5 : 4;
        for (var index = 0; index < count; index++)
        {
            if (index > 0) text.Append(',');
            if (container == 0) text.Append("\"p").Append(index).Append("\":");
            text.Append(Nested(random, depth + 1));
        }
        return text.Append(container == 0 ? '}' : ']').ToString();
    }

    private static string Primitive(Random random) => random.Next(5) switch
    {
        0 => random.Next(1000).ToString(),
        1 => "\"str" + random.Next(100) + "\"",
        2 => random.Next(2) == 0 ? "true" : "false",
        3 => "null",
        _ => (random.Next(10000) / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string Reorder(string json, Random random, double changed)
    {
        using var document = JsonDocument.Parse(json);
        var text = new StringBuilder();
        Write(document.RootElement);
        return text.ToString();

        void Write(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    text.Append('{');
                    var first = true;
                    foreach (var property in element.EnumerateObject().OrderBy(_ => random.Next()))
                    {
                        if (!first) text.Append(',');
                        first = false;
                        text.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                        Write(property.Value);
                    }
                    text.Append('}');
                    break;
                case JsonValueKind.Array:
                    text.Append('[');
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        if (index++ > 0) text.Append(',');
                        Write(item);
                    }
                    text.Append(']');
                    break;
                case JsonValueKind.Number when element.TryGetInt32(out var number) && random.NextDouble() < changed:
                    text.Append(number + 1);
                    break;
                default:
                    text.Append(element.GetRawText());
                    break;
            }
        }
    }

    private static string Elements(int count, Func<int, int> order) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(order).Select(index =>
            $"{{\"id\":{index},\"name\":\"item{index}\",\"tags\":[\"t{index % 7}\",\"u\"],\"meta\":{{\"x\":{index % 3},\"y\":\"{index}\"}}}}")) + "]";

    // Every element shares "id" and "name", shares "group" with a few others, and has one property of its own.
    private static string Shapes(int count, string side) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(index =>
            $"{{\"id\":{index},\"name\":\"n\",\"group{index % 7}\":{{\"value\":1}},\"{side}{index}\":1}}")) + "]";

    private static string Duplicates(int count, int offset) =>
        "{" + string.Join(",", Enumerable.Range(0, count).Select(index =>
            $"\"name\":{{\"id\":{index + offset},\"tags\":[\"a\",\"b{index % 5}\"],\"v\":{index % 11}}}")) + "}";

    private static string Names(int count, Func<int, string> name) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(index => "\"" + name(index) + "\"")) + "]";

    private static string Wide(int count, Func<int, int> value) =>
        "[{" + string.Join(",", Enumerable.Range(0, count).Select(index => $"\"key{index}\":{value(index)}")) + "}]";

    [Benchmark] public object CoreStrict() => JJMatcher.Match(_leftCore, _rightCore, null, Strict);
    [Benchmark] public object CoreStructureOnly() => JJMatcher.Match(_leftCore, _rightCore, null, StructureOnly);
    [Benchmark] public object NestedStrict() => JJMatcher.Match(_nested, _nestedReordered, null, Strict);
    [Benchmark] public object NestedStructureOnly() => JJMatcher.Match(_nested, _nestedReordered, null, StructureOnly);
    [Benchmark] public object NestedSelfStrict() => JJMatcher.Match(_nested, _nested, null, Strict);
    [Benchmark] public object PermutedElementsStrict() => JJMatcher.Match(_elements, _elementsPermuted, null, Strict);
    [Benchmark] public object DistinctShapesStrict() => JJMatcher.Match(_shapes, _shapesOther, null, Strict);
    [Benchmark] public object DistinctShapesStructureOnly() => JJMatcher.Match(_shapes, _shapesOther, null, StructureOnly);
    [Benchmark] public object DuplicateNamesStrict() => JJMatcher.Match(_duplicates, _duplicatesOther, null, Strict);
    [Benchmark] public object FuzzyNamesStrict() => JJMatcher.Match(_names, _namesOther, null, Fuzzy);
    [Benchmark] public object WideObjectStrict() => JJMatcher.Match(_wide, _wideOther, null, Strict);
}
