# JJMatch Guide

Compare two JSON documents, then read `IsMatch`, `Score`, and `Differences`.

```sh
dotnet add package JJMatch
```

```csharp
using JJMatch;

var expectedJson = """{"name":"Ann","age":30}""";
var actualJson = """{"name":"Ann","age":31}""";
var result = JJMatcher.Match(expectedJson, actualJson);

Console.WriteLine(result.IsMatch);       // False
Console.WriteLine($"{result.Score:F2}%"); // 66.67%
foreach (var difference in result.Differences)
    Console.WriteLine($"{difference.Path}: {difference.Kind}");
// $.age: ValueMismatch
```

`IsMatch` means there are no differences under your rules. `Score` is 0–100, not a probability. Format it with `F2`, not `P`. Differences retain paths and detached JSON values.

Defaults compare exact values, ignore object property order and array element order, and preserve repeated array elements. Strings containing JSON objects or arrays are decoded automatically. Numbers `1` and `1.0` differ by default; strings `"1"` and numbers `1` always have different types. The package targets `netstandard2.0` and `net8.0`.

## Choose a starting point

```csharp
// Ignore string case, outer whitespace and Unicode form; compare numbers by quantity.
var settings = JJMatchSettings.Normalized;
var result = JJMatcher.Match(expectedJson, actualJson, settings: settings);

// Ignore primitive values throughout the document.
var structure = JJMatcher.Match(expectedJson, actualJson,
    settings: JJMatchSettings.StructureOnly);
```

`Default`, `Normalized`, and `StructureOnly` each return a fresh, customizable settings instance. Normalized keeps property names exact and never enables fuzzy matching.

## Customize only what you need

```csharp
var settings = new JJMatchSettings
{
    StringComparison = JJStringComparison.Fuzzy,
    FuzzyThreshold = 0.8,
    NumberComparison = JJNumberComparison.Numeric,
    IgnoredPropertyPrefix = "_"
};
var result = JJMatcher.Match(expectedJson, actualJson, settings: settings);
```

| Setting | Choices and behavior |
| --- | --- |
| `Mode` | `Full` (default) or `StructureOnly` for the entire document. |
| `StringComparison` | `Exact` (default), `Normalized`, or `Fuzzy`. Fuzzy includes normalization and can also accept changed digits in identifiers. |
| `NumberComparison` | `Exact` token spelling (default) or `Numeric` exact quantity, without floating-point tolerance. |
| `PropertyNameMode` | `None` for exact names (default), or `Normalized`. Independent of string values. |
| `IgnoredPropertyPrefix` | Omit properties starting with your marker at every level. Null keeps everything; empty text is invalid. |
| `ArrayComparison` | `Unordered` (default behavior) preserves occurrences. Advanced `DistinctShapes` ignores values and repeated shapes throughout array subtrees. Neither mode uses array order. |

Identical values always match. Use the settings object before calling `Match`; do not mutate shared settings concurrently with a call.

## Save and load configuration

```csharp
File.WriteAllText("JJSetting.json", settings.ToJson());
var loaded = JJMatchSettings.FromJson(File.ReadAllText("JJSetting.json"));
var result = JJMatcher.Match(expectedJson, actualJson, settings: loaded);
```

For example, normalized text and numbers need only:

```json
{
  "StringComparison": "Normalized",
  "NumberComparison": "Numeric"
}
```

`ToJson()` emits only differences from defaults, including scope and diagnostics choices. It omits inactive fuzzy thresholds and scoring weights. Use `ToJson(includeInactiveSettings: true)` to preserve those for later editing. Equivalent legacy flags are exported as the simple string/number rules. Omitted properties use defaults. Names ignore case; unknown settings and invalid values fail validation.

## Advanced settings

```csharp
var settings = JJMatchSettings.Normalized;
settings.Mode = JJComparisonMode.Full;
settings.IncludeStructuralScore = false; // Avoid the independent structural comparison.
settings.DecodeStringifiedJson = false;  // Treat JSON-looking strings as literal strings.
settings.Limits.MaxDepth = 128;
settings.Limits.MaxAssignmentSize = 256;
settings.Scoring.Mode = JJScoringMode.Weighted;
settings.Scoring.MissingWeight = 10;
settings.Scoring.AddedWeight = 3;
```

Weights change the score, never `IsMatch` or which differences are reported. Different Missing and Added weights make the score directional. All six difference weights remain configurable, including property-name and value normalization differences. See [scoring](Docs/Scoring.md) for the formula.

`Limits` also exposes `MaxInputLength`, `MaxNodes`, and `MaxWork`. Defaults remain 512 levels, 1,000,000 UTF-16 characters per input, 100,000 nodes, 128 occurrences per assignment, and 10,000,000 work units. Assignment size cannot exceed 256. Limits fail comparison rather than returning approximate results. Very high depth can exhaust the stack.

`result.Diagnostics.StructuralScore` is an independent structure-only score, or null when disabled. `Diagnostics.ElapsedMilliseconds` includes parsing and reporting. Structural diagnostics stay enabled by default for compatibility. `Diagnostics.ValueScore` is the full score when values participate, retained for compatibility; it is not a separate component to average.

Pass `cancellationToken:` for cooperative cancellation. Invalid JSON throws `JsonException`; invalid settings throw argument exceptions; exceeded resource budgets throw `InvalidOperationException`. No partial match result is returned.

## Existing callers

Existing calls, flags, property names, enum values and defaults continue to work without deprecation warnings:

| Existing API | Preferred API |
| --- | --- |
| `options: JJMatchOptions.StructuralMatching` | `settings.Mode = JJComparisonMode.StructureOnly` |
| `options: JJMatchOptions.ValueMatching` | `Mode = Full`, `IncludeStructuralScore = false` |
| `ValueMode = Normalized | Numeric` | `StringComparison = Normalized`, `NumberComparison = Numeric` |
| `ArrayMode = Strict` / `StructureOnly` | `ArrayComparison = Unordered` / `DistinctShapes` (same behavior) |
| Top-level `MaxDepth`, `MaxNodes`, etc. | `Limits.MaxDepth`, `Limits.MaxNodes`, etc. |
| `StructuralMatchPercentage`, `ElapsedMilliseconds` | `Diagnostics.StructuralScore`, `Diagnostics.ElapsedMilliseconds` |
| `ValueMatchPercentage` | `Score` when comparing values |

An explicitly supplied legacy `options` argument overrides `settings.Mode`, even when it is `All`. `IncludeStructuralScore = false` can independently suppress structural diagnostics. In C#, old and new value/limit setters share the same state; later assignments affect the same settings. In JSON, use either legacy `ValueMode` or the new string/number properties, and either top-level limits or `Limits`; likewise use either `ArrayComparison` or `ArrayMode`. Mixing these forms is rejected to avoid order-dependent configuration.

The [detailed reference](Docs/Configuration.md) preserves the worked order example, exact score tables, old API examples and difference semantics. Try the [Playground](https://masoudimp.github.io/JJMatch/) for presets, custom rules and minimal C#/JSON exports.
