# Learn JJMatch by comparing the same documents

Change one setting, predict what will happen, then check the result. This guide uses one order in two slightly different forms to show what each rule controls. When you want more, the repository's [Examples.md](https://github.com/masoudimp/JJMatch/blob/main/Examples.md) runs every implemented behavior, including duplicates, Unicode, deep nesting, stringified input and limits, on the same test-fixture inputs under every configuration.

## 1. Quick Start

Install the package in your project:

```sh
dotnet add package JJMatch
```

Paste this small warm-up into a .NET 8 or newer console app:

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

`IsMatch` tells you whether everything matches under your rules. `Score` is already a percentage from 0 to 100. `Differences` explains what failed. A complete match always means **true, exactly 100, and an empty list**.

The default compares exact values and keeps array order. Object property order and JSON formatting do not matter. The package targets `netstandard2.0` and `net8.0`; this guide uses C# raw strings available in modern console projects.

## 2. Main Example

Imagine comparing an order saved by your application with one returned by another service. Replace the warm-up with this code. Keep these **exact same `leftJson` and `rightJson` strings** for every whole-document comparison below.

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
```

Before running anything, spot the differences:

| Location | Left → Right | What might help? |
| --- | --- | --- |
| `customer.name` | `"  Alice Chen  "` → `"alice chen"` | Normalized string comparison |
| `customer.contact` | `"Jon Smith"` → `"John Smith"` | Fuzzy string comparison |
| `items` | BOOK, PEN → PEN, BOOK | Unordered arrays |
| `total` | `30` → `30.0` | Numeric comparison |
| `paid` | `true` → `false` | A real value change |
| `delivery.days` | `3` → `5` | A real value change |
| `legacyCode` | Present JSON null → absent | A missing property |
| `coupon` | Absent → `"WELCOME"` | An additional property |

**Try predicting:** will fixing array order alone make the whole order match? What will happen if you select only Numeric?

## 3. Configuration Comparison

These results were obtained by calling the real public `JJMatcher.Match` method on the exact pair above. Scores below use invariant `"R"` formatting: the full round-trip representation of the returned `double`, with no display rounding.

| ID | Configuration | IsMatch | Exact Score (%) | Differences: count and kinds |
| --- | --- | --- | ---: | --- |
| A | Default: Strict + Exact | `false` | `50` | **11**: 9 ValueMismatch, 1 Missing, 1 Added |
| B | Unordered + Exact | `false` | `70` | **7**: 5 ValueMismatch, 1 Missing, 1 Added |
| C | StructureOnly + Exact | `false` | `64.70588235294117` | **7**: 5 ValueMismatch, 1 Missing, 1 Added |
| D | Strict + Numeric | `false` | `35` | **14**: 12 ValueMismatch, 1 Missing, 1 Added |
| E | Strict + Normalized | `false` | `55` | **10**: 8 ValueMismatch, 1 Missing, 1 Added |
| F | Strict + Fuzzy | `false` | `60` | **9**: 7 ValueMismatch, 1 Missing, 1 Added |
| G | Unordered + Exact \| Numeric \| Normalized | `false` | `80` | **5**: 3 ValueMismatch, 1 Missing, 1 Added |
| H | StructuralMatching only; default Strict arrays | `false` | `95` | **2**: 1 Missing, 1 Added |
| I | G plus Fuzzy | `false` | `85` | **4**: 2 ValueMismatch, 1 Missing, 1 Added |

There are **no TypeMismatch differences** in this pair. Every row still has the missing `legacyCode` and additional `coupon`, so no configuration produces a full match.

### Run each row

Append this small display helper after the main strings. The examples below call it; your application can simply read the result directly.

```csharp
void Show(string label, JJMatchResult comparison)
{
    string kinds = string.Join(", ", comparison.Differences
        .GroupBy(d => d.Kind).Select(g => $"{g.Count()} {g.Key}"));
    Console.WriteLine($"{label}: IsMatch={comparison.IsMatch}, " +
        $"Score={comparison.Score.ToString("R", CultureInfo.InvariantCulture)}%, " +
        $"Differences={comparison.Differences.Count} [{kinds}]");
}
```

**A — Default.** Strict pairs BOOK with PEN at position 0, then PEN with BOOK at position 1. That creates four item-value differences. Exact also rejects the name, contact and numeric spelling changes.

```csharp
var defaultResult = JJMatcher.Match(leftJson, rightJson);
Show("A", defaultResult);
```

**B — Unordered + Exact.** Both items find their exact partners, removing four differences. Counts still matter: each occurrence can match only once. JJMatch compares whole items; it does not assume `sku` or `id` is a special key.

```csharp
var unorderedResult = JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.Unordered });
Show("B", unorderedResult);
```

**C — StructureOnly + Exact.** Both item objects have the same shape, so each array contributes just one shape. All values below arrays are ignored. The five value differences outside `items` remain. Its score differs from B because fewer nodes are counted; see section 4.

```csharp
var shapeResult = JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.StructureOnly });
Show("C", shapeResult);
```

**D — Strict + Numeric.** Numeric accepts `30` and `30.0`. But Numeric **replaces** the default Exact rule: it accepts numbers only. Even the identical `orderId`, `currency`, `customer.city` and `delivery.note` strings now produce ValueMismatch. This explains the lower score. It does not convert strings into numbers.

```csharp
var numericResult = JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings { ValueMode = JJValueMatchingMode.Numeric });
Show("D", numericResult);
```

**E — Strict + Normalized.** The name now matches after trimming outer whitespace and ignoring case. Normalized accepts strings only, so numbers, booleans and nulls need Exact or another applicable rule. In this particular pair those values already differed, so there is no additional loss compared with A.

```csharp
var normalizedResult = JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings { ValueMode = JJValueMatchingMode.Normalized });
Show("E", normalizedResult);
```

**F — Strict + Fuzzy.** The name and the contact typo both match. Like Normalized, Fuzzy accepts strings only. It does not fix numeric differences or reorder the items.

```csharp
var fuzzyResult = JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings { ValueMode = JJValueMatchingMode.Fuzzy });
Show("F", fuzzyResult);
```

**G — Combine rules.** This is a useful starting point for mixed JSON when order, numeric spelling, case and outer whitespace should not matter. A value matches when **any** selected rule accepts it. The contact typo, paid status and delivery days still differ.

```csharp
var combinedResult = JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings
    {
        ArrayMode = JJArrayMatchingMode.Unordered,
        ValueMode = JJValueMatchingMode.Exact | JJValueMatchingMode.Numeric
            | JJValueMatchingMode.Normalized
    });
Show("G", combinedResult);
```

**H — StructuralMatching only.** Ignore primitive values throughout the document, keeping names, types and container structure. Strict still compares item positions, but both items have the same structural shape. Only the missing and added properties remain. This is different from the StructureOnly **array** setting in C, which ignores values only below arrays.

```csharp
var structuralResult = JJMatcher.Match(leftJson, rightJson,
    JJMatchOptions.StructuralMatching);
Show("H", structuralResult);
```

**I — Combine rules and allow typos.** Fuzzy additionally accepts the contact change. The changed boolean and number still fail, along with the missing and added properties.

```csharp
var combinedFuzzyResult = JJMatcher.Match(leftJson, rightJson,
    settings: new JJMatchSettings
    {
        ArrayMode = JJArrayMatchingMode.Unordered,
        ValueMode = JJValueMatchingMode.Exact | JJValueMatchingMode.Numeric
            | JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy
    });
Show("I", combinedFuzzyResult);
```

### Make a focused comparison change from false to true

Select a part of the **same two documents**. Nothing is edited or cleaned up. Append this helper to select a property or nested property as JSON text:

```csharp
string Part(string json, params string[] names)
{
    using var document = JsonDocument.Parse(json);
    var element = document.RootElement;
    foreach (string name in names) element = element.GetProperty(name);
    return element.GetRawText();
}
```

For example, run just the item arrays:

```csharp
Show("Items, default", JJMatcher.Match(Part(leftJson, "items"), Part(rightJson, "items")));
Show("Items, unordered", JJMatcher.Match(Part(leftJson, "items"), Part(rightJson, "items"),
    settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.Unordered }));
```

Try the other selections with the settings shown in A–I:

| Selection on both sides | Default IsMatch / Score / differences | Change | New IsMatch / Score / differences |
| --- | --- | --- | --- |
| `Part(json, "items")` | false / `42.857142857142854` / 4 ValueMismatch | Unordered + Exact (B) | true / `100` / 0 |
| `Part(json, "items")` | false / `42.857142857142854` / 4 ValueMismatch | StructureOnly + Exact (C) | true / `100` / 0 |
| `Part(json, "total")` | false / `0` / 1 ValueMismatch | `ValueMode = Exact \| Numeric` | true / `100` / 0 |
| `Part(json, "customer", "name")` | false / `0` / 1 ValueMismatch | `ValueMode = Exact \| Normalized` | true / `100` / 0 |
| `Part(json, "customer", "contact")` | false / `0` / 1 ValueMismatch | `ValueMode = Exact \| Fuzzy` | true / `100` / 0 |
| `Part(json, "customer")` | false / `50` / 2 ValueMismatch | `ValueMode = Exact \| Fuzzy` | true / `100` / 0 |
| `Part(json, "paid")` | false / `0` / 1 ValueMismatch | StructuralMatching only (H) | true / `100` / 0 |

In the table, shortened value names mean members of `JJValueMatchingMode`. Keep Exact when enabling a relaxed rule for mixed JSON. Normalized does not remove internal spaces or punctuation. Property names remain case-sensitive under every value rule.

## 4. Understanding Scores

JJMatch uses **Sørensen–Dice similarity** over matched JSON nodes:

```text
Score = 200 × matched nodes / (left nodes + right nodes)
```

A node is a JSON value. Objects and arrays count, including the root. A property name labels its value; it does not earn an extra point. A container of matching type earns one point even when its children differ. Each accepted primitive earns one point. Missing values and rejected values earn none.

Here is an independent count of the main example:

| Part | Nodes on each side |
| --- | ---: |
| Root object | 1 |
| `orderId`, `currency` | 2 |
| `customer`: object + 3 values | 4 |
| `items`: array + 2 × (object + 2 values) | 7 |
| `total`, `paid` | 2 |
| `delivery`: object + 2 values | 3 |
| `legacyCode` on Left / `coupon` on Right | 1 |
| **Total** | **20** |

For C, the two identical item shapes collapse to one: `items` counts as 4 nodes instead of 7. Each document then contributes **17** nodes. Its values outside arrays are still checked.

| ID | Matched nodes M | Left + Right nodes | Independent calculation | Why these nodes match |
| --- | ---: | ---: | --- | --- |
| A | 10 | 40 | `200 × 10 / 40 = 50` | 6 containers + orderId, currency, city, note |
| B | 14 | 40 | `200 × 14 / 40 = 70` | A + the 4 correctly paired item values |
| C | 11 | 34 | `200 × 11 / 34 = 1100/17` | 5 effective containers + 4 exact values outside arrays + 2 ignored item values |
| D | 7 | 40 | `200 × 7 / 40 = 35` | 6 containers + total |
| E | 11 | 40 | `200 × 11 / 40 = 55` | A + customer.name |
| F | 12 | 40 | `200 × 12 / 40 = 60` | A + customer.name and customer.contact |
| G | 16 | 40 | `200 × 16 / 40 = 80` | B + customer.name and total |
| H | 19 | 40 | `200 × 19 / 40 = 95` | Everything except the unpaired property on each side |
| I | 17 | 40 | `200 × 17 / 40 = 85` | G + customer.contact |

The mathematical fraction for C is `1100/17`; the actual returned double prints as `64.70588235294117`. For the focused item arrays, Strict matches the array and two objects: `M=3`, `L=R=7`, giving `42.857142857142854`. Unordered also matches all four item values, giving 100.

**What to notice:** B and C have the same differences, but different scores. Counting differences cannot reproduce a score: a missing subtree has one diagnostic but may contain many nodes, and shape mode changes what gets counted.

Use `IsMatch` to decide success. `Score == 100` and no differences mean the same thing under the selected rules. Do not round a partial score to decide that it matches. Format `Score` with `F2` and append `%` for display; .NET's `P` format would multiply it by 100 again.

Swapping Left and Right preserves Score and IsMatch. Repeated calls with the same ordered inputs, settings and runtime are deterministic. This score measures accepted JSON content and structure, not probability, business importance or factual accuracy. A fuzzy-accepted value earns full credit; fuzzy similarity is not transitive. Unicode behavior can depend on runtime Unicode data.

The overlap formula is the standard [Dice coefficient](https://docs.scipy.org/doc/scipy/reference/generated/scipy.spatial.distance.dice.html), applied to JJMatch's rules. See the [detailed scoring reference](https://github.com/masoudimp/JJMatch/blob/main/Docs/Scoring.md) for alignment constraints and limitations.

## 5. Understanding Differences

Inspect `combinedResult` from G:

```csharp
foreach (var difference in combinedResult.Differences)
{
    Console.WriteLine($"{difference.Kind}: {difference.Path}");
    Console.WriteLine($"  Left:  {difference.LeftValue?.GetRawText() ?? "<missing>"}");
    Console.WriteLine($"  Right: {difference.RightValue?.GetRawText() ?? "<missing>"}");
}
```

Its exact differences, in returned order, are:

| Kind | Path | Left value | Right value |
| --- | --- | --- | --- |
| ValueMismatch | `$.customer.contact` | `"Jon Smith"` | `"John Smith"` |
| ValueMismatch | `$.paid` | `true` | `false` |
| ValueMismatch | `$.delivery.days` | `3` | `5` |
| Missing | `$.legacyCode` | `null` | absent |
| Added | `$.coupon` | absent | `"WELCOME"` |

| Kind | Meaning |
| --- | --- |
| `ValueMismatch` | Same JSON type, but no selected value rule accepted the pair. Identical strings can appear here under Numeric-only comparison. |
| `TypeMismatch` | Different JSON types, such as a boolean versus a number. |
| `Missing` | Exists on the left, absent on the right. |
| `Added` | Exists only on the right. |

`$` is the current comparison root. `$.items[0].sku` points to the first item's SKU; indices start at zero. When comparing a selected subtree, its path starts again at `$`. Names with punctuation are escaped, such as `$['first.name']`.

`Path` is the left location when present, otherwise the right location. `LeftPath` and `RightPath` retain original input positions separately; they can differ with unordered pairing. Swapping inputs can change diagnostic ordering and tied pair choices even though the score stays the same.

A missing subtree or a type mismatch produces one difference at that subtree's root. Value snapshots contain the whole affected subtree and remain usable after `Match` returns. `Differences` is read-only. A nullable missing snapshot is different from present JSON null: here `legacyCode` has `LeftValue.Value.ValueKind == JsonValueKind.Null`, while its `RightValue` is nullable null.

## 6. Advanced Scenarios

These small cases isolate details that the main order does not contain.

### Duplicate property names

Duplicates are kept as separate occurrences and matched one-to-one, even with Strict arrays. Try this standalone pair after the earlier examples:

```csharp
var duplicates = JJMatcher.Match(
    """{"entry":{"value":1},"entry":{"value":2}}""",
    """{"entry":{"value":2},"entry":{"value":3}}""");
Show("Duplicates", duplicates);
```

| IsMatch | Score | Differences | LeftPath → RightPath | Values |
| --- | ---: | --- | --- | --- |
| false | `80` | 1 ValueMismatch | `$.entry#1.value` → `$.entry#2.value` | `1` → `3` |

The two `value:2` occurrences pair together. Both documents have 5 nodes; 4 match, so `200 × 4 / 10 = 80`. The `#1` and `#2` suffixes are one-based occurrence numbers. These paths are diagnostics, not JSON Patch instructions or executable JSONPath queries.

### Unicode: same-looking strings can have different encodings

The first string below uses precomposed `é`; the second uses `e` plus a combining accent. They are JSON text, including their quotes.

```csharp
string composed = "\"caf\\u00E9\"";
string decomposed = "\"cafe\\u0301\"";
Show("Unicode, exact", JJMatcher.Match(composed, decomposed));
Show("Unicode, normalized", JJMatcher.Match(composed, decomposed,
    settings: new JJMatchSettings
    { ValueMode = JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized }));
```

| Configuration | IsMatch | Score | Differences |
| --- | --- | ---: | --- |
| Exact | false | `0` | 1 ValueMismatch at `$` |
| Exact \| Normalized | true | `100` | 0 |

Normalized uses Unicode NFC and case-insensitive comparison. It does not remove accents or normalize property names. Exact compares decoded string contents, so merely spelling the same character as a JSON escape does not cause a mismatch. Numeric tokens work differently: Exact preserves their written spelling.

### Fuzzy boundaries: a short typo may still fail

Fuzzy allows `floor(longer folded UTF-16 length / 5)` edits. An edit is an insertion, deletion, replacement or adjacent swap, using optimal string alignment distance. Folding trims outer whitespace, applies supported Unicode normalization and ignores case.

Every row below first returned **false / 0 / 1 ValueMismatch** under Exact. Here are the results with `Exact | Fuzzy`:

| Left JSON string | Right JSON string | Allowed edits | IsMatch | Score | Differences |
| --- | --- | ---: | --- | ---: | --- |
| `"Jon"` | `"John"` | 0 | false | `0` | 1 ValueMismatch |
| `"Smith"` | `"Smyth"` | 1 | true | `100` | 0 |
| `"Jon Smith"` | `"John Smith"` | 2 | true | `100` | 0 |
| `"SKU-10000"` | `"SKU-10001"` | 1 | true | `100` | 0 |

Run a row by passing its JSON string literals:

```csharp
Show("Short name, fuzzy", JJMatcher.Match("\"Jon\"", "\"John\"",
    settings: new JJMatchSettings
    { ValueMode = JJValueMatchingMode.Exact | JJValueMatchingMode.Fuzzy }));
```

Fuzzy can accept changed digits inside strings; prefer Exact for identifiers. It does not give a partially accepted string a fractional score. UTF-16 units are not always whole visible characters, and a chain of fuzzy matches does not guarantee the endpoints match.

### Stringified JSON: decode one outer layer

Sometimes an API returns JSON encoded inside a JSON string. Such input is recognized and decoded without any setting. This example wraps the main Left document without changing its contents:

```csharp
string encodedLeft = JsonSerializer.Serialize(leftJson);
Show("Encoded Left vs Left", JJMatcher.Match(encodedLeft, leftJson));
Show("Encoded Left vs Right", JJMatcher.Match(encodedLeft, rightJson));
```

| Comparison | IsMatch | Score | Differences |
| --- | --- | ---: | --- |
| Encoded Left vs plain Left | true | `100` | 0 |
| Encoded Left vs plain Right | false | `50` | Same 11 differences as A |

A root string is decoded only when its content is a JSON object or array, and only one outer layer is decoded. Ordinary object/array roots stay unchanged, strings nested inside a document remain literal values, and any other root string, such as `"hello"`, `"123"` or `"{"`, is compared as the string it is.

### Resource limits and input mistakes

The main pair has 40 total nodes before shape reduction. Intentionally set a lower limit:

```csharp
try
{
    JJMatcher.Match(leftJson, rightJson,
        settings: new JJMatchSettings { MaxNodes = 39 });
}
catch (InvalidOperationException exception)
{
    Console.WriteLine(exception.Message); // Comparison exceeded JJMatchSettings.MaxNodes.
}
```

These outcomes were checked against the same main pair. Each row changes only the stated settings; all other settings keep their defaults.

| Settings | Actual outcome |
| --- | --- |
| `MaxNodes = 40` | Normal result: false / `50` / 11 differences, as A |
| `MaxNodes = 39` | `InvalidOperationException` for MaxNodes |
| `MaxDepth = 2` | `JsonException` (the item objects require depth 3) |
| `ArrayMode = Unordered, MaxAssignmentSize = 1` | `InvalidOperationException` for MaxAssignmentSize |
| `MaxWork = 1` | `InvalidOperationException` for MaxWork |
| `MaxInputLength = 1` | `InvalidOperationException` for MaxInputLength |

Defaults allow 1,000,000 UTF-16 characters per input, depth 64, 100,000 total nodes, 128 occurrences per side in an unordered or duplicate-property group, and a comparison work budget of 10,000,000. Settings can raise limits; depth is capped at 128, total nodes at 1,000,000 and assignment groups at 256. The work budget is not a time limit. Limit failures never return approximate scores.

`Match` also accepts `cancellationToken:` and cooperatively throws `OperationCanceledException` when cancelled. Configure settings before calling; do not mutate shared settings during a call.

Pass JSON text, not a file path or a C# object's `ToString()`. Use `File.ReadAllText(path)` for files and `JsonSerializer.Serialize(value)` for objects. Any valid JSON root is accepted. C# `null` throws `ArgumentNullException`; the JSON text `"null"` represents a valid null value. Empty text, comments, trailing commas and malformed JSON throw `JsonException`.

### Options and result properties

Unknown option bits and invalid settings throw argument exceptions. `ValueMode.None` is rejected when value comparison is enabled.

`JJMatchOptions.All` is zero and requests default structure and value comparison. `ValueMatching` alone still includes the structure needed to compare values, while omitting the separate structural diagnostic. Arrays are always compared under `ArrayMode`, and differences are always returned.

`StructuralMatchPercentage` is an independent structure-only diagnostic; `ValueMatchPercentage` is the value-aware document score, not a component to average with the structural score. The public entry point is `Match`, not `Compare`.

## 7. Next Steps

[Examples.md](https://github.com/masoudimp/JJMatch/blob/main/Examples.md) continues from here. Each of its examples takes one section of the library's test fixtures and shows how the same two inputs behave under all fifteen array, value and structural configurations, with the exact `IsMatch`, `Score` and differences returned, so you can see which setting changes which outcome before applying it to your own documents.
