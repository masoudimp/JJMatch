using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace JJMatch.Benchmarks;

// Run with: dotnet run -c Release --project benchmarks/JJMatch.Benchmarks -- --filter "*StructureMatcher*"
// The trees are built in the setup, so the figures are those of StructureMatcher.Match alone.
[MemoryDiagnoser]
public class StructureMatcherBenchmarks
{
    private JsonStructureNode _left = null!;
    private JsonStructureNode _right = null!;

    [GlobalSetup]
    public void Setup()
    {
        _left = Fixture("Left.json");
        _right = Fixture("Right.json");
    }

    private static JsonStructureNode Fixture(string name)
    {
        using var stream = typeof(StructureMatcherBenchmarks).Assembly.GetManifestResourceStream(name)!;
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 128 });
        return JsonStructureTraverser.Traverse(document.RootElement.GetProperty("core"));
    }

    [Benchmark]
    public object LeftAgainstRight() => StructureMatcher.Match(_left, _right);

    [Benchmark]
    public object LeftAgainstItself() => StructureMatcher.Match(_left, _left);
}

// Synthetic documents, generated here because the fixtures hold no large group. Each is one array of objects.
[MemoryDiagnoser]
public class StructureMatcherGroupBenchmarks
{
    private JsonStructureNode _left = null!;
    private JsonStructureNode _right = null!;
    private JsonStructureNode _repeated = null!;

    // The number of distinct element shapes on each side, none of them on both sides: the size of the group to pair.
    [Params(8, 32, 128, 512)]
    public int Shapes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _left = Tree(Shapes, index => $"\"left{index}\": 1");
        _right = Tree(Shapes, index => $"\"right{index}\": 1");
        _repeated = Tree(Shapes * 100, _ => "\"same\": 1");
    }

    // Every element shares "id" and "name", shares "group" with a few others, and has one property of its own.
    private static JsonStructureNode Tree(int count, Func<int, string> ownProperty)
    {
        var json = new StringBuilder("[");
        for (var index = 0; index < count; index++)
        {
            json.Append(index == 0 ? "" : ",")
                .Append($"{{\"id\": {index}, \"name\": \"n\", \"group{index % 7}\": {{\"value\": 1}}, {ownProperty(index)}}}");
        }
        using var document = JsonDocument.Parse(json.Append(']').ToString());
        return JsonStructureTraverser.Traverse(document.RootElement);
    }

    // Every pair of shapes is compared and the whole group is assigned at once, whatever its size.
    [Benchmark]
    public object DistinctShapes() => StructureMatcher.Match(_left, _right);

    // One hundred times as many elements, all of one shape on both sides: nothing is left over to pair.
    [Benchmark]
    public object RepeatedShape() => StructureMatcher.Match(_repeated, _repeated);
}
