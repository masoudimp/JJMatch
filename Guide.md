# JJMatch Guide

Change one setting, predict what will happen, then check the result. This guide compares one order in two slightly different forms to show what each rule controls. To experiment with your own JSON, use the [JJMatch Playground](https://masoudimp.github.io/JJMatch/): it runs the library in the browser and shows the equivalent `JJMatcher.Match` call and `JJSetting.json` for the selected settings.

## 1. Quick Start

```sh
dotnet add package JJMatch
```

```csharp
using System;
using JJMatch;

var result = JJMatcher.Match(
    "{\"name\":\"Ann\",\"age\":30}",
    "{\"name\":\"Ann\",\"age\":31}");

Console.WriteLine(result.IsMatch);           // False
Console.WriteLine($"{result.Score:F2}%");    // 66.67% in an English culture
foreach (var difference in result.Differences)
    Console.WriteLine($"{difference.Path}: {difference.Kind}");
// $.age: ValueMismatch
```

`IsMatch` tells you whether everything matches under your rules. `Score` is a percentage from 0 to 100. `Differences` explains what failed. A complete match always means **true, exactly 100, and an empty list**.

The default compares exact values and counts every array element. Array element order, object property order and JSON formatting do not matter. The package targets `netstandard2.0` and `net8.0`; the examples use C# raw strings.

## 2. The Example Documents

Imagine comparing an order saved by your application with one returned by another service. Every result below uses these exact two strings.

```csharp
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using JJMatch;

string leftJson = """
{
  "orderId": "ORD-1042",
  "currency": "USD",
  "customer": { "name": "  Alice Chen  ", "city": "Seattle", "contact": "Jon Smith" },
  "items": [{ "sku": "BOOK", "quantity": 1 }, { "sku": "PEN", "quantity": 2 }],
  "total": 30,
  "paid": true,
  "delivery": { "days": 3, "note": "Leave at door" },
  "legacyCode": null
}
""";

string rightJson = """
{
  "orderId": "ORD-1042",
  "currency": "USD",
  "customer": { "name": "alice chen", "city": "Seattle", "contact": "John Smith" },
  "items": [{ "sku": "PEN", "quantity": 2 }, { "sku": "BOOK", "quantity": 1 }],
  "total": 30.0,
  "paid": false,
  "delivery": { "days": 5, "note": "Leave at door" },
  "coupon": "WELCOME"
}
""";

void Show(string label, JJMatchResult comparison)
{
    string kinds = string.Join(", ", comparison.Differences
        .GroupBy(d => d.Kind).Select(g => $"{g.Count()} {g.Key}"));
    Console.WriteLine($"{label}: IsMatch={comparison.IsMatch}, " +
        $"Score={comparison.Score.ToString("R", CultureInfo.InvariantCulture)}%, " +
        $"Differences={comparison.Differences.Count} [{kinds}]");
}
```

The differences between the two documents, and the setting that could accept each one:

| Location | Left → Right | What might help? |
| --- | --- | --- |
| `customer.name` | `"  Alice Chen  "` → `"alice chen"` | Normalized string comparison |
| `customer.contact` | `"Jon Smith"` → `"John Smith"` | Fuzzy string comparison |
| `items` | BOOK, PEN → PEN, BOOK | Nothing needed: array order never matters |
| `total` | `30` → `30.0` | Numeric comparison |
| `paid` | `true` → `false` | A real value change |
| `delivery.days` | `3` → `5` | A real value change |
| `legacyCode` | Present JSON null → absent | A missing property |
| `coupon` | Absent → `"WELCOME"` | An additional property |

## 3. Settings

Settings are passed as `JJMatchSettings`. `ArrayMode` controls how arrays are paired, `ValueMode` is a set of flags that decides which primitive values are accepted, `PropertyNameMode` decides how property names are paired, and `Scoring` decides how the score is computed from the result. `JJMatchOptions.StructuralMatching` ignores primitive values altogether.

```csharp
Show("A", JJMatcher.Match(leftJson, rightJson));

Show("B", JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.StructureOnly }));

Show("F", JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings
    {
        ValueMode = JJValueMatchingMode.Numeric
            | JJValueMatchingMode.Normalized
    }));

Show("G", JJMatcher.Match(leftJson, rightJson, JJMatchOptions.StructuralMatching));
```

Results on the example pair. Scores use invariant `"R"` formatting, the full round-trip representation of the returned `double`.

| ID | Configuration | IsMatch | Score (%) | Differences |
| --- | --- | --- | ---: | --- |
| A | Default: Strict, identical values | `false` | `70` | **7**: 5 ValueMismatch, 1 Missing, 1 Added |
| B | StructureOnly | `false` | `64.70588235294117` | **7**: 5 ValueMismatch, 1 Missing, 1 Added |
| C | Strict + Numeric | `false` | `75` | **6**: 4 ValueMismatch, 1 Missing, 1 Added |
| D | Strict + Normalized | `false` | `75` | **6**: 4 ValueMismatch, 1 Missing, 1 Added |
| E | Strict + Fuzzy | `false` | `80` | **5**: 3 ValueMismatch, 1 Missing, 1 Added |
| F | Strict + Numeric \| Normalized | `false` | `80` | **5**: 3 ValueMismatch, 1 Missing, 1 Added |
| G | StructuralMatching, Strict arrays | `false` | `95` | **2**: 1 Missing, 1 Added |
| H | F plus Fuzzy | `false` | `85` | **4**: 2 ValueMismatch, 1 Missing, 1 Added |

Every row keeps the missing `legacyCode` and the added `coupon`, so no setting produces a full match. What each row shows:

- **A, Strict arrays.** Array elements pair wherever they stand, so BOOK and PEN each find their exact partner although the two documents list them in a different order. Counts still matter: every occurrence can match only once, and JJMatch compares whole items rather than treating `sku` or `id` as a key.
- **B, StructureOnly arrays.** Both item objects have the same shape, so each array contributes one shape and the values below arrays are ignored. The score differs from A because fewer nodes are counted (section 4).
- **C, Numeric.** `30` and `30.0` now match. Numeric relaxes numbers only. Identical values **always** match, whatever is selected: a rule only accepts further pairs, so the identical strings here still match.
- **D, Normalized.** Strings match after trimming outer white space, Unicode NFC normalization and ignoring case. Internal spaces, punctuation and accents are kept. Strings only.
- **E, Fuzzy.** Small typos are accepted: two strings match when their similarity, one minus the edits per character of the longer folded text, is at least `FuzzyThreshold`, where an edit is an insertion, deletion, replacement or adjacent swap. The default threshold is 0.8, one edit for every five characters: `"Jon"` vs `"John"` allows 0 edits and fails; `"Jon Smith"` vs `"John Smith"` allows 2 and matches. Set `FuzzyThreshold = 0.75` and `"Jon"` matches `"John"` too; set it to 1 and only Normalized-equal strings match. Fuzzy can accept changed digits inside strings, so leave it off for identifiers. Strings only.
- **F and H, combined rules.** A value matches when **any** selected rule accepts it. This is a practical starting point for mixed JSON where numeric spelling, case and outer white space should not matter.
- **G, StructuralMatching.** Names, types and container structure are compared, primitive values are ignored. Unlike the StructureOnly array mode in B, this applies to the whole document.

Value rules never touch property names. Identical names always pair, and the default `JJPropertyNameMatchingMode.None` pairs nothing else. `PropertyNameMode = JJPropertyNameMatchingMode.Normalized` pairs names with the same trimming, NFC and case-insensitive rule as Normalized values, so `name` pairs with `Name` while `postalCode` still does not pair with `postal_code`. It applies under every option, including `StructuralMatching`, and names of one object that become equal under it are treated as duplicate property names.

```csharp
Show("Names, default", JJMatcher.Match("""{"name":"Ali"}""", """{"Name":"Ali"}"""));
// false / 50 / Missing $.name, Added $.Name
Show("Names, normalized", JJMatcher.Match("""{"name":"Ali"}""", """{"Name":"Ali"}""",
    settings: new JJMatchSettings { PropertyNameMode = JJPropertyNameMatchingMode.Normalized }));
// true / 100 / 0
```

### Scoring weights

By default every node counts the same, so a missing field, an extra field and a wrong value each cost one node of the score. `JJMatchSettings.Scoring` is always present, never null, and selects `JJScoringMode.Standard`, which is exactly that score. Switch it to `JJScoringMode.Weighted` to make some kinds of difference matter less than others: each `JJDifferenceKind` gets an importance weight from 1 (least important) to 10 (most critical), and an unmatched node then counts its kind's weight against a matched node's 10. The defaults are `TypeMismatchWeight = 10`, `MissingWeight = 8`, `ValueMismatchWeight = 6` and `AddedWeight = 3`: a wrong type breaks the shape a consumer relies on, missing content was expected, a wrong value keeps the shape, and extra content leaves everything expected in place.

```csharp
Show("W", JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted } }));

Show("X", JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings
    {
        Scoring =
        {
            Mode = JJScoringMode.Weighted,
            MissingWeight = 10,
            AddedWeight = 10,
            ValueMismatchWeight = 1
        }
    }));
```

The same differences, weighed differently, on the example pair. The unmatched mass is the sum over the differences of their nodes times their kind's weight; the matched mass is 20 per matched node.

| ID | Configuration | IsMatch | Score (%) | Differences | Unmatched mass against matched mass |
| --- | --- | --- | ---: | --- | --- |
| A | Strict, Standard | `false` | `70` | 5 ValueMismatch, 1 Missing, 1 Added | 12 nodes of 40, unweighted |
| W | A, Weighted with the default weights | `false` | `79.77207977207978` | the same 7 | `5 × 2 × 6 + 1 × 8 + 1 × 3 = 71` against `14 × 20 = 280` |
| X | A, Weighted: Missing 10, Added 10, ValueMismatch 1 | `false` | `90.3225806451613` | the same 7 | `10 + 10 + 10 = 30` against `280` |
| Y | A, Weighted: Missing 1, Added 1, ValueMismatch 10 | `false` | `73.29842931937172` | the same 7 | `100 + 1 + 1 = 102` against `280` |
| G+W | StructuralMatching, Weighted defaults | `false` | `97.18670076726343` | 1 Missing, 1 Added | `8 + 3 = 11` against `380` |

What to expect from the weights:

- **`IsMatch` and the differences never change.** Weights change the number, not the verdict: a complete match is 100 under every mode, and any difference keeps the score below 100.
- **A weight of 10 is the Standard cost.** With every weight at 10 the Weighted score equals the Standard one exactly. Lower weights discount a kind, so a Weighted score is never below the Standard score, and lowering a weight never lowers the score.
- **A subtree weighs its size.** A missing object of five nodes with `MissingWeight = 8` costs 40; a wrong value costs its two primitives times `ValueMismatchWeight`; a type mismatch costs the nodes of both sides. Nothing is counted twice: every unmatched node belongs to exactly one difference.
- **Direction matters when Missing and Added differ.** Swapping the two documents turns every Missing into an Added, so with the default weights `[1,2]` against `[1,2,3]` scores `95.238…` and `[1,2,3]` against `[1,2]` scores `88.235…`. Give both kinds the same weight when the direction should not matter.
- **Validation.** A weight outside 1 to 10 or an unknown mode throws `ArgumentOutOfRangeException`, and setting `Scoring` to null throws `ArgumentNullException`.

### Settings from a JJSetting.json file

Settings can also be kept in a JSON file and loaded with `JJMatchSettings.FromJson`. Each property is named like the `JJMatchSettings` property, ignoring case; an omitted property keeps its default. Modes are member names, value rules are joined by `|`, `FuzzyThreshold` is a number from 0 to 1, limits are integers, and `Scoring` is an object whose properties are named like those of `JJScoringSettings`. The playground shows this file for the settings you select.

```json
{
  "ArrayMode": "Strict",
  "ValueMode": "Numeric",
  "PropertyNameMode": "None",
  "FuzzyThreshold": 0.8,
  "MaxDepth": 128,
  "Scoring": { "Mode": "Weighted", "MissingWeight": 8, "AddedWeight": 3, "TypeMismatchWeight": 10, "ValueMismatchWeight": 6 }
}
```

```csharp
var settings = JJMatchSettings.FromJson(File.ReadAllText("JJSetting.json"));
var result = JJMatcher.Match(leftJson, rightJson, settings: settings);
```

An unknown name, a value of the wrong type or a limit outside its range throws an argument exception; text that is not JSON throws `JsonException`.

## 4. Understanding Scores

JJMatch uses **Sørensen–Dice similarity** over matched JSON nodes:

```text
Score = 200 × matched nodes / (left nodes + right nodes)
```

A node is a JSON value. Objects and arrays count, including the root. A property name labels its value and does not earn a point of its own. A container of matching type earns one point even when its children differ. Each accepted primitive earns one point. Missing and rejected values earn none.

The example documents have 20 nodes each: the root, `orderId`, `currency`, `customer` (object + 3 values), `items` (array + 2 × (object + 2 values)), `total`, `paid`, `delivery` (object + 2 values), and `legacyCode` or `coupon`. Under B the two identical item shapes collapse to one, so each side has 17.

| ID | Matched | Left + Right | Calculation | Which nodes match |
| --- | ---: | ---: | --- | --- |
| A | 14 | 40 | `200 × 14 / 40 = 70` | 6 containers + orderId, currency, city, note + the 4 item values |
| B | 11 | 34 | `200 × 11 / 34 = 1100/17` | 5 effective containers + 4 exact values outside arrays + 2 ignored item values |
| C | 15 | 40 | `200 × 15 / 40 = 75` | A + total |
| D | 15 | 40 | `200 × 15 / 40 = 75` | A + customer.name |
| E | 16 | 40 | `200 × 16 / 40 = 80` | A + customer.name and customer.contact |
| F | 16 | 40 | `200 × 16 / 40 = 80` | A + customer.name and total |
| G | 19 | 40 | `200 × 19 / 40 = 95` | Everything except the unpaired property on each side |
| H | 17 | 40 | `200 × 17 / 40 = 85` | F + customer.contact |

A and B report the same differences but different scores: counting differences cannot reproduce a score, because a missing subtree is one diagnostic but may contain many nodes, and shape mode changes what is counted.

Under `JJScoringMode.Weighted` (section 3) the same matched and unmatched nodes are weighed instead of counted: each matched node weighs 10 on each side, and each unmatched node weighs the 1-to-10 weight of the kind of difference it belongs to.

```text
Weighted score = 100 × 20 × matched nodes / (20 × matched nodes + Σ weight × unmatched nodes of each difference)
```

Row W above is `100 × 280 / (280 + 71)`. With every weight at 10 this is the Standard formula again.

Use `IsMatch` to decide success, not a rounded score. Format `Score` with `F2` and append `%`; .NET's `P` format would multiply it by 100 again. Swapping Left and Right preserves `Score` and `IsMatch`. The score measures accepted content and structure, not probability or factual accuracy. See the [scoring reference](https://github.com/masoudimp/JJMatch/blob/main/Docs/Scoring.md) for alignment constraints and limitations.

## 5. Understanding Differences

```csharp
var combined = JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings
    {
        ValueMode = JJValueMatchingMode.Numeric
            | JJValueMatchingMode.Normalized
    });

foreach (var difference in combined.Differences)
{
    Console.WriteLine($"{difference.Kind} (weight {difference.ScoreWeight}): {difference.Path}");
    Console.WriteLine($"  Left:  {difference.LeftValue?.GetRawText() ?? "<missing>"}");
    Console.WriteLine($"  Right: {difference.RightValue?.GetRawText() ?? "<missing>"}");
}
```

| Kind | ScoreWeight | Path | Left value | Right value |
| --- | ---: | --- | --- | --- |
| ValueMismatch | 10 | `$.customer.contact` | `"Jon Smith"` | `"John Smith"` |
| ValueMismatch | 10 | `$.paid` | `true` | `false` |
| ValueMismatch | 10 | `$.delivery.days` | `3` | `5` |
| Missing | 10 | `$.legacyCode` | `null` | absent |
| Added | 10 | `$.coupon` | absent | `"WELCOME"` |

`ScoreWeight` is the importance weight the score applied to the difference, from 1 to 10. Under the default `Standard` scoring every difference counts fully, so it is always 10. Under `Weighted` it is the configured weight of the difference's `Kind`, so the same list with the default weights would show 6, 6, 6, 8 and 3.

| Kind | Meaning |
| --- | --- |
| `ValueMismatch` | Same JSON type, but no selected value rule accepted the pair. |
| `TypeMismatch` | Different JSON types, such as a boolean versus a number. |
| `Missing` | Exists on the left, absent on the right. |
| `Added` | Exists only on the right. |

`$` is the comparison root, indices start at zero (`$.items[0].sku`), names with punctuation are escaped (`$['first.name']`), and duplicate property names get one-based occurrence suffixes (`$.entry#2`). `Path` is the left location when present, otherwise the right one; `LeftPath` and `RightPath` keep the original positions on each side and can differ, because array elements pair wherever they stand. A missing subtree or a type mismatch produces one difference at the subtree's root. Snapshots contain the whole affected subtree and stay usable after `Match` returns. A nullable missing snapshot is different from present JSON null: `legacyCode` has `LeftValue.Value.ValueKind == JsonValueKind.Null` and a null `RightValue`.

## 6. Input, Limits and Errors

**Input.** Pass JSON text, not a file path or an object's `ToString()`. Any valid JSON root is accepted. A root string whose content is a JSON object or array is decoded one layer automatically, so a stringified document compares equal to its plain form; strings nested inside a document stay literal. C# `null` throws `ArgumentNullException`; empty text, comments, trailing commas and malformed JSON throw `JsonException`.

**Limits.** Defaults allow 1,000,000 UTF-16 characters per input, depth 64, 100,000 total nodes, 128 elements per array and 128 occurrences per duplicate-property group, and a comparison work budget of 10,000,000. Settings can raise them: depth without an upper bound, total nodes up to 1,000,000 and arrays and duplicate-property groups up to 256 (`MaxAssignmentSize`), so a Strict array of more than 256 elements cannot be compared. The comparison recurses once per nesting level, so a document deep enough for a very large depth can overflow the stack, which cannot be caught. Exceeding a limit throws `InvalidOperationException` (or `JsonException` for depth); a limit failure never returns an approximate score.

```csharp
try
{
    JJMatcher.Match(leftJson, rightJson, settings: new JJMatchSettings { MaxNodes = 39 });
}
catch (InvalidOperationException exception)
{
    Console.WriteLine(exception.Message); // Comparison exceeded JJMatchSettings.MaxNodes.
}
```

**Cancellation and validation.** `Match` accepts `cancellationToken:` and throws `OperationCanceledException` when cancelled. Unknown option bits and invalid settings throw argument exceptions. `ValueMode.None`, the default, is valid: only identical values match. Configure settings before calling and do not mutate shared settings during a call.

**Result properties.** `StructuralMatchPercentage` is an independent structure-only diagnostic, computed under the selected scoring mode; `ValueMatchPercentage` is the value-aware document score, not a component to average with the structural one. `ElapsedMilliseconds` is the wall-clock time the call took, including parsing and difference reporting.

## 7. Next Steps

Paste your own documents into the [JJMatch Playground](https://masoudimp.github.io/JJMatch/), switch the settings, and copy the generated `JJMatcher.Match` call or `JJSetting.json` into your project.
