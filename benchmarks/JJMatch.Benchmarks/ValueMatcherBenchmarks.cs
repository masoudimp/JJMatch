using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace JJMatch.Benchmarks;

// Synthetic inputs are confined to benchmarks. Parsing and tree construction are outside the measured operation.
[MemoryDiagnoser]
public class FuzzyValueBenchmarks
{
    private JsonStructureNode _left = null!;
    private JsonStructureNode _equal = null!;
    private JsonStructureNode _oneEdit = null!;

    [Params(32, 256, 2048)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _left = Node(new string('a', Length));
        _equal = Node(new string('A', Length));
        _oneEdit = Node(new string('a', Length - 1) + "b");
        if (!EqualAfterFolding() || !OneEdit())
        {
            throw new InvalidOperationException("Benchmark inputs must match under the documented fuzzy rule.");
        }
    }

    private static JsonStructureNode Node(string text) =>
        new(null, JsonValueKind.String, text, Array.Empty<JsonStructureNode>());

    [Benchmark]
    public bool EqualAfterFolding() => ValueMatcher.MatchValues(_left, _equal, JJValueMatchingMode.Fuzzy);

    [Benchmark]
    public bool OneEdit() => ValueMatcher.MatchValues(_left, _oneEdit, JJValueMatchingMode.Fuzzy);
}

[MemoryDiagnoser]
public class NumericExponentBenchmarks
{
    private JsonStructureNode _left = null!;
    private JsonStructureNode _right = null!;

    [Params(16, 256, 4096)]
    public int ExponentDigits { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var exponent = new string('9', ExponentDigits);
        using var left = JsonDocument.Parse("1e" + exponent);
        using var right = JsonDocument.Parse("1.0e" + exponent);
        _left = JsonStructureTraverser.Traverse(left.RootElement);
        _right = JsonStructureTraverser.Traverse(right.RootElement);
        if (!EquivalentNumbers())
        {
            throw new InvalidOperationException("Benchmark inputs denote the same exact quantity.");
        }
    }

    [Benchmark]
    public bool EquivalentNumbers() => ValueMatcher.MatchValues(_left, _right, JJValueMatchingMode.Numeric);
}

// Equivalent functionality only: exact numeric quantity equality on pre-parsed scalar numbers. This says nothing
// about document matching, differences, parsing throughput, or the proposed Strict/Unordered public modes.
[MemoryDiagnoser]
public class NumericComparisonBenchmarks
{
    private JsonElement _left;
    private JsonElement _right;
    private JsonStructureNode _leftNode = null!;
    private JsonStructureNode _rightNode = null!;

    [Params("integerDecimal", "beyondDouble", "outsideDecimal")]
    public string Scenario { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _left = Number("Left.json");
        _right = Number("Right.json");
        _leftNode = JsonStructureTraverser.Traverse(_left);
        _rightNode = JsonStructureTraverser.Traverse(_right);
        var expected = Scenario != "beyondDouble";
        if (SystemTextJson() != expected || JJMatchNumeric() != expected)
        {
            throw new InvalidOperationException("The benchmark requires equivalent numeric semantics.");
        }
    }

    private JsonElement Number(string fixture)
    {
        using var stream = typeof(NumericComparisonBenchmarks).Assembly.GetManifestResourceStream(fixture)!;
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.GetProperty("core").GetProperty("numericValues").GetProperty(Scenario).Clone();
    }

    [Benchmark(Baseline = true)]
    public bool SystemTextJson() => JsonElement.DeepEquals(_left, _right);

    [Benchmark]
    public bool JJMatchNumeric() => ValueMatcher.MatchValues(_leftNode, _rightNode, JJValueMatchingMode.Numeric);
}
