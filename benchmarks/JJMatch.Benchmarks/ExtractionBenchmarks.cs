using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace JJMatch.Benchmarks;

// Run with: dotnet run -c Release --project benchmarks/JJMatch.Benchmarks
// The cost of building the JsonStructureNode tree from a parsed document.
[MemoryDiagnoser]
public class ExtractionBenchmarks
{
    private JsonDocument _document = null!;
    private JsonElement _root;

    [Params("Left.json", "Right.json")]
    public string Fixture { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(ExtractionBenchmarks).Assembly.GetManifestResourceStream(Fixture)!;
        _document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 128 });
        _root = _document.RootElement.GetProperty("core");
    }

    [GlobalCleanup]
    public void Cleanup() => _document.Dispose();

    [Benchmark]
    public object Traverse() => JsonStructureTraverser.Traverse(_root);

    // Without arguments every benchmark class of the project runs; --filter selects some of them.
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(ExtractionBenchmarks).Assembly).Run(args.Length == 0 ? ["--filter", "*"] : args);
}
