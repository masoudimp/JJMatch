# JJMatch

## [Try JJMatch Playground →](https://masoudimp.github.io/JJMatch/)

Compare your own JSON in the browser, explore every setting, and copy the equivalent `JJMatcher.Match` call and `JJSetting.json`.

## Get started

Compare two JSON documents in one call. Get a clear match result, useful differences, and a similarity percentage.

```sh
dotnet add package JJMatch --version 0.1.0-preview.1
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

Start with a preset and customize only what you need:

```csharp
var settings = JJMatchSettings.Normalized;
settings.IgnoredPropertyPrefix = "_";
var result = JJMatcher.Match(expectedJson, actualJson, settings: settings);
```

Use `JJMatchSettings.StructureOnly` to ignore primitive values, or `Default` for the original exact-value behavior. Each preset returns a fresh object. String and number rules are independent; advanced scoring, resource limits and diagnostics remain available. Save minimal configuration with `settings.ToJson()` and reload it with `JJMatchSettings.FromJson(...)`.

**Read the [guide](Guide.md)** for the settings, how the score is computed, and what each difference means.

## Behavior

- Identical values and count-preserving arrays by default. Array element order, object property order and JSON formatting are ignored.
- Optional structural shape comparison of arrays, Normalized, Numeric or Fuzzy value rules on top of identical values, which always match, with a configurable fuzzy similarity threshold, a Normalized property name rule that pairs `name` with `Name`, and a property prefix of your choice, such as `_`, `*` or `ignore_`, whose properties (`_comment`, `*tokens`, `ignore_reason`) are removed from both documents before comparing.
- Read-only differences with paths and detached JSON snapshots, including original array indices and duplicate-property occurrences.
- Deterministic Sørensen–Dice node similarity (symmetric under Standard scoring) from 0 to 100, with an optional weighted mode in which each kind of difference counts from 1 to 10. `IsMatch` is true exactly when `Score` is 100 and `Differences` is empty, under every mode. See [scoring](Docs/Scoring.md).
- Automatic decoding of a stringified JSON object or array, as the root or as a value at any level, an opt-out via `DecodeStringifiedJson = false`, cancellation and configurable resource limits. Invalid options and settings throw rather than being silently ignored.

Useful for comparing expected and actual API responses, test data, or structured LLM output. Similar JSON does not necessarily mean a factually correct answer.

The package targets **netstandard2.0 and net8.0**. Pull requests, main pushes and tagged releases validate on Windows, macOS and Linux, with tests on .NET 8 and .NET 10 and a separate consumer smoke test of the packed NuGet package. The current version is `0.1.0-preview.1`.

## Development

```sh
dotnet build -c Release
dotnet test tests/JJMatch.Tests -c Release
dotnet pack src/JJMatch -c Release
```

Install both .NET 8 and .NET 10 runtimes to run both test targets. Benchmarks live in `benchmarks/JJMatch.Benchmarks`, with recorded measurements under `benchmarks/results`. The playground source is in `samples/JJMatch.Playground`.

Design details: [decisions](Docs/Decisions.md), [matching](Docs/Match.md), [value rules](Docs/MatchValues.md), and the [historical technical review](Docs/TechnicalReview.md).

Licensed under [MIT](LICENSE).

To verify the packed artifact locally (requires both runtimes), use a fresh package cache:

```sh
dotnet pack src/JJMatch -c Release -o artifacts
dotnet restore tests/JJMatch.PackageSmoke/JJMatch.PackageSmoke.proj --configfile tests/JJMatch.PackageSmoke/NuGet.Config --packages ./artifacts/consumer-cache
dotnet run --project tests/JJMatch.PackageSmoke/JJMatch.PackageSmoke.proj -c Release -f net8.0 --no-restore
dotnet run --project tests/JJMatch.PackageSmoke/JJMatch.PackageSmoke.proj -c Release -f net10.0 --no-restore
```

The consumer is intentionally outside the solution and uses `PackageReference`, not a project reference. For another version, pass `-p:PackageVersion=<version>` to restore and run.
