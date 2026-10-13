# JJMatch

Compare two JSON documents in one call. Get a clear match result, useful differences, and a similarity percentage.

```sh
dotnet add package JJMatch
```

```csharp
using System;
using JJMatch;

var result = JJMatcher.Match(
    "{\"name\":\"Ann\",\"age\":30}",
    "{\"name\":\"Ann\",\"age\":31}");

Console.WriteLine(result.IsMatch);        // False
Console.WriteLine($"{result.Score:F2}%"); // 66.67%

foreach (var difference in result.Differences)
    Console.WriteLine($"{difference.Path}: {difference.Kind}");
// $.age: ValueMismatch
```

Start with the **[consumer guide](Guide.md)** for installation, complete examples, options, arrays, nested JSON, and expected error behavior. Then see **[Examples.md](Examples.md)**: every implemented behavior, from arrays and value rules to duplicates, Unicode, stringified input and limits, run on the same fixture sections under every configuration, with the exact scores and differences the library returns.

## Behavior

- Exact values and ordered, count-preserving arrays by default. Object property order and JSON formatting are ignored.
- Optional unordered arrays, structural shape comparison, and Exact, Normalized, Numeric or Fuzzy value rules.
- Read-only differences with paths and detached JSON snapshots, including original array indices and duplicate-property occurrences.
- Deterministic, symmetric Sørensen–Dice node similarity from 0 to 100. `IsMatch` is true exactly when `Score` is 100 and `Differences` is empty. Containers earn structural credit; the score is not a probability or domain-specific accuracy measure. See [scoring semantics and research](Docs/Scoring.md).
- Automatic decoding of a stringified JSON object or array root, cancellation and configurable resource limits. Invalid options and settings throw rather than being silently ignored.

Useful for comparing expected and actual API responses, test data, or structured LLM output. Similar JSON does not necessarily mean a factually correct answer.

The package targets **netstandard2.0 and net8.0**. Tests exercise both shipped assemblies on .NET 8 and .NET 10. CI is configured for Windows, macOS and Linux. Other compatible runtimes, trimming and Native AOT require separate validation.

## Development

```sh
dotnet build -c Release
dotnet test tests/JJMatch.Tests -c Release
dotnet pack src/JJMatch -c Release
```

Install both .NET 8 and .NET 10 runtimes to run both test targets. Benchmarks live in `benchmarks/JJMatch.Benchmarks`; recorded measurements are under `benchmarks/results`. They measure internal components and, with `--filter "*JJMatcher*"`, the public `Match` call end to end over the fixtures and generated documents.

Design details: [decisions](Docs/Decisions.md), [matching](Docs/Match.md), [value rules](Docs/MatchValues.md), and the [historical technical review](Docs/TechnicalReview.md).

Licensed under [MIT](LICENSE).
