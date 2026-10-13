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

**Try it in the browser:** the [JJMatch Playground](https://masoudimp.github.io/JJMatch/) runs the library on your own JSON, lets you switch every setting, and shows the equivalent `JJMatcher.Match` call and `JJSetting.json`.

**Read the [guide](Guide.md)** for the settings, how the score is computed, and what each difference means.

## Behavior

- Exact values and ordered, count-preserving arrays by default. Object property order and JSON formatting are ignored.
- Optional unordered arrays, structural shape comparison, Exact, Normalized, Numeric or Fuzzy value rules with a configurable fuzzy similarity threshold, and a Normalized property name rule that pairs `name` with `Name`.
- Read-only differences with paths and detached JSON snapshots, including original array indices and duplicate-property occurrences.
- Deterministic, symmetric Sørensen–Dice node similarity from 0 to 100. `IsMatch` is true exactly when `Score` is 100 and `Differences` is empty. See [scoring](Docs/Scoring.md).
- Automatic decoding of a stringified JSON object or array root, cancellation and configurable resource limits. Invalid options and settings throw rather than being silently ignored.

Useful for comparing expected and actual API responses, test data, or structured LLM output. Similar JSON does not necessarily mean a factually correct answer.

The package targets **netstandard2.0 and net8.0**. Tests run on .NET 8 and .NET 10; CI covers Windows, macOS and Linux.

## Development

```sh
dotnet build -c Release
dotnet test tests/JJMatch.Tests -c Release
dotnet pack src/JJMatch -c Release
```

Install both .NET 8 and .NET 10 runtimes to run both test targets. Benchmarks live in `benchmarks/JJMatch.Benchmarks`, with recorded measurements under `benchmarks/results`. The playground source is in `samples/JJMatch.Playground`.

Design details: [decisions](Docs/Decisions.md), [matching](Docs/Match.md), [value rules](Docs/MatchValues.md), and the [historical technical review](Docs/TechnicalReview.md).

Licensed under [MIT](LICENSE).
