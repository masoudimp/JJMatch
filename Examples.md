# JJMatch Examples

Every example on this page compares one small section of `Left.json` with the same section of `Right.json`, the two fixtures that drive the JJMatch test suite. The inputs of an example never change; only the configuration does. Every `IsMatch`, `Score` and difference shown here was produced by running the public `JJMatcher.Match` method on those exact inputs. Nothing is estimated.

Read the [consumer guide](Guide.md) first if you are new to JJMatch. This page goes further than the guide: it covers each implemented behavior, from basic value comparison to Unicode, duplicate property names, deep nesting, stringified input and resource limits, and it shows how the same two documents behave under every relevant configuration.

The fixtures live in the repository at `tests/JJMatch.Tests/TestData/ComplexJson/`. Each example names its section, for example `publicApi.basic`, and shows the section text from both files. You can paste the two JSON snippets into strings instead of loading the files: JSON formatting never affects a result.

## 1. Setup

### 1.1 Helper code

The examples use this small set of helpers in a .NET 8 or newer console app. `Section` selects a part of a fixture as JSON text; strings are property names and integers are array indices. `Settings` builds a `JJMatchSettings`, and `Show` prints the three outputs of a comparison. Scores are printed with the invariant `"R"` format, the exact round-trip text of the returned `double`.

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using JJMatch;
using static JJMatch.JJArrayMatchingMode;
using static JJMatch.JJValueMatchingMode;

string leftJson = File.ReadAllText("tests/JJMatch.Tests/TestData/ComplexJson/Left.json");
string rightJson = File.ReadAllText("tests/JJMatch.Tests/TestData/ComplexJson/Right.json");

string Section(string json, params object[] path)
{
    // MaxDepth 128 is needed only to parse the whole fixture: one stress section is 100 levels deep.
    using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
    var element = document.RootElement;
    foreach (var step in path)
        element = step is int index ? element[index] : element.GetProperty((string)step);
    return element.GetRawText();
}

JJMatchSettings Settings(JJArrayMatchingMode arrays, JJValueMatchingMode values) =>
    new JJMatchSettings { ArrayMode = arrays, ValueMode = values };

void Show(string id, JJMatchResult result)
{
    string differences = string.Join(", ", result.Differences.Select(d => $"{d.Kind} {d.Path}"));
    Console.WriteLine($"{id}: IsMatch={result.IsMatch}, " +
        $"Score={result.Score.ToString("R", CultureInfo.InvariantCulture)}, " +
        $"Count={result.Differences.Count} [{differences}]");
}
```

### 1.2 The configurations

Every example runs the same fifteen configurations. Each has a letter, used in every results table below. `Exact`, `Numeric`, `Normalized` and `Fuzzy` are members of `JJValueMatchingMode`; `Strict`, `Unordered` and `StructureOnly` are members of `JJArrayMatchingMode`. Rows M, N and O pass `JJMatchOptions.StructuralMatching`, which ignores primitive values entirely; all other rows compare values.

| ID | Arrays | Values | Call |
| --- | --- | --- | --- |
| A | Strict | Exact | `JJMatcher.Match(left, right)` (the default) |
| B | Unordered | Exact | `JJMatcher.Match(left, right, settings: Settings(Unordered, Exact))` |
| C | StructureOnly | Exact | `JJMatcher.Match(left, right, settings: Settings(StructureOnly, Exact))` |
| D | Strict | Numeric only | `JJMatcher.Match(left, right, settings: Settings(Strict, Numeric))` |
| E | Strict | Normalized only | `JJMatcher.Match(left, right, settings: Settings(Strict, Normalized))` |
| F | Strict | Fuzzy only | `JJMatcher.Match(left, right, settings: Settings(Strict, Fuzzy))` |
| G | Strict | Exact \| Numeric | `JJMatcher.Match(left, right, settings: Settings(Strict, Exact \| Numeric))` |
| H | Strict | Exact \| Normalized | `JJMatcher.Match(left, right, settings: Settings(Strict, Exact \| Normalized))` |
| I | Strict | Exact \| Fuzzy | `JJMatcher.Match(left, right, settings: Settings(Strict, Exact \| Fuzzy))` |
| J | Strict | all four rules | `JJMatcher.Match(left, right, settings: Settings(Strict, Exact \| Numeric \| Normalized \| Fuzzy))` |
| K | Unordered | all four rules | `JJMatcher.Match(left, right, settings: Settings(Unordered, Exact \| Numeric \| Normalized \| Fuzzy))` |
| L | StructureOnly | all four rules | `JJMatcher.Match(left, right, settings: Settings(StructureOnly, Exact \| Numeric \| Normalized \| Fuzzy))` |
| M | Strict | ignored | `JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching)` |
| N | Unordered | ignored | `JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching, Settings(Unordered, Exact))` |
| O | StructureOnly | ignored | `JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching, Settings(StructureOnly, Exact))` |

To run every row for one pair, add this list and loop:

```csharp
var all = Exact | Numeric | Normalized | Fuzzy;
var configurations = new (string Id, JJMatchOptions? Options, JJMatchSettings Settings)[]
{
    ("A", null, Settings(Strict, Exact)),          ("B", null, Settings(Unordered, Exact)),
    ("C", null, Settings(StructureOnly, Exact)),   ("D", null, Settings(Strict, Numeric)),
    ("E", null, Settings(Strict, Normalized)),     ("F", null, Settings(Strict, Fuzzy)),
    ("G", null, Settings(Strict, Exact | Numeric)), ("H", null, Settings(Strict, Exact | Normalized)),
    ("I", null, Settings(Strict, Exact | Fuzzy)),  ("J", null, Settings(Strict, all)),
    ("K", null, Settings(Unordered, all)),         ("L", null, Settings(StructureOnly, all)),
    ("M", JJMatchOptions.StructuralMatching, Settings(Strict, Exact)),
    ("N", JJMatchOptions.StructuralMatching, Settings(Unordered, Exact)),
    ("O", JJMatchOptions.StructuralMatching, Settings(StructureOnly, Exact)),
};

void RunAll(string left, string right)
{
    foreach (var (id, options, settings) in configurations)
        Show(id, JJMatcher.Match(left, right, options, settings));
}
```

Each example then needs only two `Section` calls and `RunAll(left, right)`; the example also shows the single call for the configuration that changes the outcome.

### 1.3 How to read the tables

- Configurations that return the same `IsMatch`, the same `Score` and the same differences share one row. The first column lists their letters.
- `Score` is the exact returned value. `Count` is `Differences.Count`. The last column names each difference's kind and `Path`; long lists are shown under the table.
- A complete match always means `IsMatch` true, `Score` exactly 100 and no differences. Those three never disagree.
- Numeric, Normalized and Fuzzy **replace** Exact when used alone (rows D, E, F). Normalized and Fuzzy accept strings only, Numeric accepts numbers only, so an identical number, Boolean or null becomes a `ValueMismatch` under E or F, and an identical string under D. Example 2 shows this once; later examples list D, E and F only where they add something and otherwise note the rows they join. For real work combine a relaxed rule with Exact, as rows G to L do.
- Under rows M, N and O values are ignored, so only `TypeMismatch`, `Missing` and `Added` can appear.

## 2. Basics

### Example 1: one changed value

Section `publicApi.basic`.

```json
{ "name": "Ann", "age": 30 }
```

```json
{ "name": "Ann", "age": 31 }
```

```csharp
var left = Section(leftJson, "publicApi", "basic");
var right = Section(rightJson, "publicApi", "basic");
Show("A", JJMatcher.Match(left, right));
Show("M", JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C E F G H I J K L | false | `66.66666666666667` | 1 | ValueMismatch `$.age` |
| D | false | `33.333333333333336` | 2 | ValueMismatch `$.name`, ValueMismatch `$.age` |
| M N O | true | `100` | 0 | |

Each document has three nodes: the object, `name` and `age`. Under A two of them match, so the score is `200 × 2 / 6`. No rule can make `30` equal `31`, so every value-comparing row stays false. Numeric alone (D) also rejects the identical strings `"Ann"`, which is why its score halves. Array mode does not matter because there is no array. Ignoring values (M, N and O) leaves only names and types, which are identical.

The default result also reports `StructuralMatchPercentage = 100` and `ValueMatchPercentage = 66.66666666666667`. The difference carries detached snapshots: `LeftValue` is `30` and `RightValue` is `31`, and its `ToString()` is `ValueMismatch: Different values at $.age and $.age.`

### Example 2: identical documents, and why a relaxed rule alone is a trap

Section `publicApi.basic` from `Left.json`, compared with itself.

```csharp
var left = Section(leftJson, "publicApi", "basic");
Show("A", JJMatcher.Match(left, left));
Show("E", JJMatcher.Match(left, left, settings: Settings(Strict, Normalized)));
Show("H", JJMatcher.Match(left, left, settings: Settings(Strict, Exact | Normalized)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C G H I J K L M N O | true | `100` | 0 | |
| D | false | `66.66666666666667` | 1 | ValueMismatch `$.name` |
| E F | false | `66.66666666666667` | 1 | ValueMismatch `$.age` |

The two inputs are byte for byte the same, yet D, E and F report a difference. Numeric has no rule for strings, so `"Ann"` against `"Ann"` is rejected. Normalized and Fuzzy have no rule for numbers, so `30` against `30` is rejected. A rule only ever says yes to the JSON type it understands, and a value is accepted when **any** selected rule accepts it. Keep `Exact` in the mode whenever the documents hold mixed types; the Exact | Normalized row (H) is true.

### Example 3: property order and formatting never matter

Section `core.propertyOrder`.

```json
{ "head": { "alpha": 1, "tail": { "alpha": 2, "beta": 3 } }, "foot": 4 }
```

```json
{ "foot": 4, "head": { "tail": { "beta": 3, "alpha": 2 }, "alpha": 1 } }
```

```csharp
var left = Section(leftJson, "core", "propertyOrder");
var right = Section(rightJson, "core", "propertyOrder");
Show("A", JJMatcher.Match(left, right));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C D G H I J K L M N O | true | `100` | 0 | |
| E F | false | `42.857142857142854` | 4 | ValueMismatch `$.head.alpha`, `$.head.tail.alpha`, `$.head.tail.beta`, `$.foot` |

Object properties are paired by name at every level, in any mode, so the reversed order is a complete match. Rows E and F fail only because all four values are numbers (see Example 2). The `stress.whitespace` section, whose two sides differ only in tabs, line breaks and spaces, gives exactly the same result under A: true, `100`, no differences.

### Example 4: different root types and empty containers

Three small pairs show how containers earn credit. Every configuration returns the same result for each pair, because there are no values to relax and no array positions to reorder.

| Section | Left | Right | IsMatch | Score | Count | Differences |
| --- | --- | --- | --- | ---: | ---: | --- |
| `publicApi.emptyArray` vs `publicApi.emptyObject` | `[]` | `{}` | false | `0` | 1 | TypeMismatch `$` |
| `core.customer.preferences` | `{}` | `{ "newsletter": true }` | false | `66.66666666666667` | 1 | Added `$.newsletter` |
| `core.customer.tags` | `[]` | `["vip"]` | false | `66.66666666666667` | 1 | Added `$[0]` |

```csharp
Show("A", JJMatcher.Match(Section(leftJson, "publicApi", "emptyArray"), Section(rightJson, "publicApi", "emptyObject")));
Show("C", JJMatcher.Match(Section(leftJson, "core", "customer", "tags"), Section(rightJson, "core", "customer", "tags"),
    settings: Settings(StructureOnly, Exact)));
```

An array against an object is a type mismatch at the root; nothing below a type mismatch is compared, and no node matches, so the score is `0`. An empty container against a container with one child still earns the shared container: one of three nodes is unmatched, `200 × 1 / 3`. The empty array stays a difference even under StructureOnly (C, L, O): an empty array holds no element shape, so `[]` and `["vip"]` have different shapes.

## 3. Reading differences

### Example 5: the four kinds of difference in one document

Section `publicApi.nested`.

```json
{ "profile": { "city": "Oslo" }, "tags": ["a", "b"], "old": true, "nullable": null }
```

```json
{ "profile": { "city": "Rome" }, "tags": ["b", "a"], "new": [1] }
```

```csharp
var left = Section(leftJson, "publicApi", "nested");
var right = Section(rightJson, "publicApi", "nested");
Show("A", JJMatcher.Match(left, right));
Show("B", JJMatcher.Match(left, right, settings: Settings(Unordered, Exact)));
Show("M", JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A D E F G H I J | false | `37.5` | 6 | ValueMismatch `$.profile.city`, ValueMismatch `$.tags[0]`, ValueMismatch `$.tags[1]`, Missing `$.old`, Missing `$.nullable`, Added `$.new` |
| B K | false | `62.5` | 4 | ValueMismatch `$.profile.city`, Missing `$.old`, Missing `$.nullable`, Added `$.new` |
| C L | false | `57.142857142857146` | 4 | the same four as B |
| M N | false | `75` | 3 | Missing `$.old`, Missing `$.nullable`, Added `$.new` |
| O | false | `71.42857142857143` | 3 | the same three as M |

Each side has eight nodes. Under A only the root object, `profile` and `tags` match: `200 × 3 / 16 = 37.5`. Unordered (B) adds the two tag strings: `200 × 5 / 16 = 62.5`. StructureOnly (C) collapses the two string elements of each `tags` array into one shape, so each side has seven effective nodes and four of them match: `200 × 4 / 14`. B and C report identical differences but different scores because they count different nodes.

Read the difference snapshots for the null and the array:

| Difference | `LeftValue` | `RightValue` | `LeftPath` | `RightPath` |
| --- | --- | --- | --- | --- |
| Missing `$.nullable` | a `JsonElement` whose `ValueKind` is `Null` | C# `null` | `$.nullable` | `null` |
| Added `$.new` | C# `null` | `[1]` | `null` | `$.new` |

Present JSON `null` is a node that can be missing; absence is not a value. A missing or added subtree such as `[1]` is one difference at its root, however many nodes it holds. Under Strict arrays, `["a","b"]` against `["b","a"]` is two value differences at `$.tags[0]` and `$.tags[1]`.

### Example 6: a subtree that changed type

Section `core.payment`.

```json
{
  "method": "card",
  "amount": 228.99,
  "currency": "USD",
  "captured": true,
  "card": {
    "brand": "visa",
    "last4": "4242",
    "expiry": { "month": 12, "year": 2027 }
  },
  "refunds": []
}
```

```json
{
  "method": "card",
  "amount": 218.99,
  "currency": "USD",
  "captured": true,
  "card": {
    "brand": "visa",
    "last4": "4242",
    "expiry": "12/2027"
  },
  "refunds": [
    { "id": "re-1", "amount": 10, "reason": null }
  ]
}
```

```csharp
var left = Section(leftJson, "core", "payment");
var right = Section(rightJson, "core", "payment");
Show("A", JJMatcher.Match(left, right));
Show("E", JJMatcher.Match(left, right, settings: Settings(Strict, Normalized)));
Show("M", JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C G H I J K L | false | `61.53846153846154` | 3 | ValueMismatch `$.amount`, TypeMismatch `$.card.expiry`, Added `$.refunds[0]` |
| E F | false | `53.84615384615385` | 4 | the three above plus ValueMismatch `$.captured` |
| D | false | `23.076923076923077` | 8 | the three above plus ValueMismatch `$.method`, `$.currency`, `$.captured`, `$.card.brand`, `$.card.last4` |
| M N O | false | `69.23076923076923` | 2 | TypeMismatch `$.card.expiry`, Added `$.refunds[0]` |

The left document has 12 nodes and the right has 14 (the refund object adds four). Under A eight nodes match: the root, `method`, `currency`, `captured`, `card`, `brand`, `last4` and the empty-then-not `refunds` array, so `200 × 8 / 26`. The `expiry` object against the string `"12/2027"` is a single `TypeMismatch`; its `month` and `year` are never compared and earn nothing. The added refund is a single `Added` difference with a five-node snapshot. Ignoring values (M) regains only `amount`. Numeric alone (D) is the worst row because it rejects every string and Boolean in the document.

Array mode never changes this example: the only array is `refunds`, and an empty array against a one-element array is the same difference under Strict, Unordered and StructureOnly.

### Example 7: values that look alike but are not

The `stress.lookalikeValues.nullVsStringVsMissing` array holds eight one-property objects. Comparing elements from the two files shows that JJMatch never converts between JSON types. Each row compares a left element with a right element; every configuration from A to L agrees, so the table shows one result per pair and notes where M, N and O differ.

| Left element | Right element | IsMatch | Score | Count | Differences | StructuralMatching |
| --- | --- | --- | ---: | ---: | --- | --- |
| `{ "v": null }` | `{ "v": "null" }` | false | `50` | 1 | TypeMismatch `$.v` | same |
| `{ "v": null }` | `{ "v": 0 }` | false | `50` | 1 | TypeMismatch `$.v` | same |
| `{ "v": 0 }` | `{ "v": false }` | false | `50` | 1 | TypeMismatch `$.v` | same |
| `{ "v": [] }` | `{ "v": {} }` | false | `50` | 1 | TypeMismatch `$.v` | same |
| `{ "v": "" }` | `{ "v": "null" }` | false | `50` | 1 | ValueMismatch `$.v` | true, `100`, 0 |
| `{ "v": null }` | `{}` | false | `66.66666666666667` | 1 | Missing `$.v` | same |
| `{ "v": "" }` | `{}` | false | `66.66666666666667` | 1 | Missing `$.v` | same |

```csharp
string Element(string json, int index) => Section(json, "stress", "lookalikeValues", "nullVsStringVsMissing", index);
Show("A", JJMatcher.Match(Element(leftJson, 0), Element(rightJson, 1)));
Show("M", JJMatcher.Match(Element(leftJson, 2), Element(rightJson, 1), JJMatchOptions.StructuralMatching));
```

Only two strings are a `ValueMismatch`, so only that pair becomes a match once values are ignored. Everything else differs in JSON type, and a type difference survives every configuration. No value rule accepts `"null"` for `null`: a JSON type difference is never a value match.

## 4. Arrays

### Example 8: reordered primitives

Section `publicApi.arrayValues`.

```json
[1, 2, 3]
```

```json
[3, 2, 1]
```

```csharp
var left = Section(leftJson, "publicApi", "arrayValues");
var right = Section(rightJson, "publicApi", "arrayValues");
Show("A", JJMatcher.Match(left, right));
Show("B", JJMatcher.Match(left, right, settings: Settings(Unordered, Exact)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A D G H I J | false | `50` | 2 | ValueMismatch `$[0]`, ValueMismatch `$[2]` |
| B C K L M N O | true | `100` | 0 | |
| E F | false | `25` | 3 | ValueMismatch `$[0]`, `$[1]`, `$[2]` |

Strict pairs elements by position, so `1` meets `3` and `3` meets `1`; the middle `2` matches. Four nodes a side, two matched: `200 × 2 / 8 = 50`. Unordered finds the one-to-one pairing that matches the most nodes. StructureOnly sees one number shape on each side. Under M, N and O every number is the same structural type, so positions no longer matter either.

### Example 9: an extra element

Section `publicApi.surplus`.

```json
[1, 2]
```

```json
[1, 2, 3]
```

```csharp
var left = Section(leftJson, "publicApi", "surplus");
var right = Section(rightJson, "publicApi", "surplus");
Show("A", JJMatcher.Match(left, right));
Show("C", JJMatcher.Match(left, right, settings: Settings(StructureOnly, Exact)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B D G H I J K M N | false | `85.71428571428571` | 1 | Added `$[2]` |
| C L O | true | `100` | 0 | |
| E F | false | `28.571428571428573` | 3 | ValueMismatch `$[0]`, ValueMismatch `$[1]`, Added `$[2]` |

Strict and Unordered both preserve element counts, so the surplus `3` is `Added` and the sizes three and four give `200 × 3 / 7`. StructureOnly compares the **set of element shapes** and ignores both count and order: each side has exactly one shape, a number, so the arrays are a complete match. Choose StructureOnly only when "an array of numbers" is all you need to know.

### Example 10: same shape, different values and counts

Section `publicApi.shapeValues`.

```json
[{ "id": 1 }, { "id": 2 }]
```

```json
[{ "id": 9 }]
```

```csharp
var left = Section(leftJson, "publicApi", "shapeValues");
var right = Section(rightJson, "publicApi", "shapeValues");
Show("A", JJMatcher.Match(left, right));
Show("M", JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching));
Show("C", JJMatcher.Match(left, right, settings: Settings(StructureOnly, Exact)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B D E F G H I J K | false | `50` | 2 | ValueMismatch `$[0].id`, Missing `$[1]` |
| C L O | true | `100` | 0 | |
| M N | false | `75` | 1 | Missing `$[1]` |

Five nodes on the left, three on the right. Strict and Unordered pair the first objects (the array and the object match, `id` does not) and report the second left object as `Missing`: `200 × 2 / 8`. StructureOnly reduces both arrays to one shape, "an object with a number `id`", and ignores every value below an array, even though Exact is selected. Rows M and N ignore values everywhere but still count elements, so only the missing object remains.

### Example 11: unordered objects and where a difference is reported

Section `publicApi.arrayObjects`.

```json
[{ "id": 1, "name": "A" }, { "id": 2, "name": "B" }]
```

```json
[{ "id": 2, "name": "B" }, { "id": 1, "name": "X" }]
```

```csharp
var left = Section(leftJson, "publicApi", "arrayObjects");
var right = Section(rightJson, "publicApi", "arrayObjects");
Show("A", JJMatcher.Match(left, right));
var unordered = JJMatcher.Match(left, right, settings: Settings(Unordered, Exact));
Show("B", unordered);
Console.WriteLine($"{unordered.Differences[0].LeftPath} -> {unordered.Differences[0].RightPath}");
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A D E F G H I J | false | `42.857142857142854` | 4 | ValueMismatch `$[0].id`, `$[0].name`, `$[1].id`, `$[1].name` |
| B K | false | `85.71428571428571` | 1 | ValueMismatch `$[0].name` |
| C L M N O | true | `100` | 0 | |

Under Strict the objects are compared crosswise, and all four values differ. Unordered pairs `{2, B}` with `{2, B}` and `{1, A}` with `{1, X}`, the assignment that matches six of seven nodes a side. The one remaining difference has `LeftPath = $[0].name` and `RightPath = $[1].name`: `Path` shows the left location, and both original indices stay available. JJMatch does not treat `id` as a key; it compares whole elements and keeps the best pairing.

### Example 12: a mixed array, and why true versus false is a value difference

Section `core.primitives`.

```json
["text", 42, 3.5, true, false, null]
```

```json
["text", "42", 3.5, false, null]
```

```csharp
var left = Section(leftJson, "core", "primitives");
var right = Section(rightJson, "core", "primitives");
Show("A", JJMatcher.Match(left, right));
Show("B", JJMatcher.Match(left, right, settings: Settings(Unordered, Exact)));
Show("M", JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A G H I J | false | `46.15384615384615` | 4 | TypeMismatch `$[1]`, ValueMismatch `$[3]`, TypeMismatch `$[4]`, Missing `$[5]` |
| B K | false | `76.92307692307692` | 2 | TypeMismatch `$[1]`, Missing `$[3]` |
| C L O | true | `100` | 0 | |
| D | false | `30.76923076923077` | 5 | the four of A plus ValueMismatch `$[0]` |
| E F | false | `30.76923076923077` | 5 | the four of A plus ValueMismatch `$[2]` |
| M | false | `61.53846153846154` | 3 | TypeMismatch `$[1]`, TypeMismatch `$[4]`, Missing `$[5]` |
| N | false | `76.92307692307692` | 2 | TypeMismatch `$[1]`, Missing `$[4]` |

Seven nodes against six. `42` against `"42"` is a `TypeMismatch` in every row: number and string are different types. `true` against `false` at `$[3]` is a **ValueMismatch**, because JJMatch treats `true` and `false` as one Boolean type; it disappears in M, where values are ignored. `false` against `null` at `$[4]` is a `TypeMismatch` that survives everything. Unordered (B) pairs `false` with `false` and `null` with `null`, leaving `true` without a partner. Under N the leftover Boolean is reported at `$[4]` instead of `$[3]`: tied choices are resolved from the left in document order, so `true` takes the only Boolean partner and `false` is left over. StructureOnly sees the same four shapes on both sides: string, number, Boolean and null.

### Example 13: nested arrays

Section `core.matrix`.

```json
[
  [1, 2, 3],
  [4, 5, 6],
  []
]
```

```json
[
  [1, 2, 3],
  [4, 5],
  [],
  [7, [8, 9]]
]
```

```csharp
var left = Section(leftJson, "core", "matrix");
var right = Section(rightJson, "core", "matrix");
Show("A", JJMatcher.Match(left, right));
Show("C", JJMatcher.Match(left, right, settings: Settings(StructureOnly, Exact)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B D G H I J K M N | false | `75` | 2 | Missing `$[1][2]`, Added `$[3]` |
| C L O | false | `66.66666666666667` | 1 | Added `$[3]` |
| E F | false | `33.333333333333336` | 7 | the two of A plus ValueMismatch `$[0][0]`, `$[0][1]`, `$[0][2]`, `$[1][0]`, `$[1][1]` |

Ten nodes on the left and fourteen on the right. Under A nine match, so `200 × 9 / 24 = 75`. The missing `6` is reported at its nested position `$[1][2]`, and the whole `[7, [8, 9]]` is one `Added` difference. Unordered gives the same result here because the shared rows already sit at the same positions. StructureOnly removes the missing `6` (counts inside `[4, 5, 6]` and `[4, 5]` are ignored) but still reports `[7, [8, 9]]`: an array holding a number and a nested array is a shape the left side lacks. Its score is lower than A's although it reports fewer differences; shape mode counts fewer nodes.

### Example 14: a heterogeneous array with nested objects

Section `core.history`.

```json
[
  "created",
  42,
  true,
  null,
  { "event": "paid", "at": "2024-03-01T10:15:00Z", "meta": { "gateway": "stripe", "attempt": 1 } },
  ["retry", 2, { "event": "webhook", "meta": { "status": 200 } }],
  []
]
```

```json
[
  "created",
  "42",
  true,
  { "event": "paid", "at": "2024-03-01T10:15:00Z", "meta": { "gateway": "stripe" } },
  ["retry", 2, { "event": "webhook", "meta": { "status": 200, "body": {} } }],
  null
]
```

```csharp
var left = Section(leftJson, "core", "history");
var right = Section(rightJson, "core", "history");
Show("A", JJMatcher.Match(left, right));
Show("B", JJMatcher.Match(left, right, settings: Settings(Unordered, Exact)));
Show("C", JJMatcher.Match(left, right, settings: Settings(StructureOnly, Exact)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A G H I J M | false | `16.216216216216218` | 5 | TypeMismatch `$[1]`, `$[3]`, `$[4]`, `$[5]`, Missing `$[6]` |
| B K N | false | `86.48648648648648` | 4 | TypeMismatch `$[1]`, Missing `$[4].meta.attempt`, Added `$[4][2].meta.body`, Missing `$[6]` |
| C L O | false | `88.88888888888889` | 4 | Missing `$[1]`, Missing `$[4].meta.attempt`, Added `$[4][2].meta.body`, Missing `$[6]` |
| D | false | `5.405405405405405` | 7 | the five of A plus ValueMismatch `$[0]`, `$[2]` |
| E F | false | `10.81081081081081` | 6 | the five of A plus ValueMismatch `$[2]` |

Nineteen nodes against eighteen. Removing one `null` shifted every later element on the right, so Strict pairs the object with a `null`, the inner array with the object and so on: four type mismatches, only three nodes matched, `200 × 3 / 37`. Unordered pairs each element with the best partner of the same type and then compares inside: it finds the missing `attempt`, the added `body` (reported at its right-side location `$[4][2].meta.body`) and the empty array nobody has on the right. Sixteen nodes match. StructureOnly pairs shapes: the right side has no number shape, so `42` is `Missing`, while `"42"` shares the string shape with `"created"`.

The scores of A and B differ by seventy points although both documents are the same. When arrays are lists of records rather than sequences, Unordered is usually the right policy.

### Example 15: shifted positions around null

Section `stress.containers.sparseLike`.

```json
[1, null, null, null, 5]
```

```json
[1, null, 5]
```

```csharp
var left = Section(leftJson, "stress", "containers", "sparseLike");
var right = Section(rightJson, "stress", "containers", "sparseLike");
Show("A", JJMatcher.Match(left, right));
Show("B", JJMatcher.Match(left, right, settings: Settings(Unordered, Exact)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A G H I J M | false | `60` | 3 | TypeMismatch `$[2]`, Missing `$[3]`, Missing `$[4]` |
| B K N | false | `80` | 2 | Missing `$[2]`, Missing `$[3]` |
| C L O | true | `100` | 0 | |
| D | false | `40` | 4 | the three of A plus ValueMismatch `$[1]` |
| E F | false | `20` | 5 | the three of A plus ValueMismatch `$[0]`, `$[1]` |

Under Strict, `null` at position 2 meets `5`, a type mismatch, and `5` at position 4 has no partner. Unordered matches `1`, `5` and one `null`, and reports the two surplus nulls as `Missing`. StructureOnly sees a number shape and a null shape on both sides. Present nulls are real nodes with real positions; they are not holes.

### Example 16: polymorphic objects

Section `stress.polymorphism`.

```json
[
  { "type": "circle", "radius": 1 },
  { "type": "rect", "w": 2, "h": 3 },
  { "type": "group", "members": [{ "type": "circle", "radius": 0.5 }, { "type": "group", "members": [] }] },
  { "type": "Circle", "radius": "1" },
  { "type": ["circle", "rect"] },
  { "type": null },
  { "TYPE": "circle" },
  { "kind": "circle", "type": "rect", "__typename": "Group", "$type": "System.String", "@type": "Thing" },
  {}
]
```

```json
[
  { "type": "circle", "radius": "1" },
  { "type": "group", "members": [{ "type": "circle", "radius": 0.5 }, { "type": "group", "members": [] }] },
  { "type": "rect", "w": 2, "h": 3 },
  { "type": "Circle", "radius": "1" },
  { "type": ["circle", "rect"] },
  { "type": null },
  { "TYPE": "circle" },
  { "kind": "circle", "type": "rect", "__typename": "Group", "$type": "System.String", "@type": "Thing" },
  {}
]
```

Two things changed: the first element's `radius` became the string `"1"`, and the `rect` and `group` elements swapped places.

```csharp
var left = Section(leftJson, "stress", "polymorphism");
var right = Section(rightJson, "stress", "polymorphism");
Show("A", JJMatcher.Match(left, right));
Show("B", JJMatcher.Match(left, right, settings: Settings(Unordered, Exact)));
Show("C", JJMatcher.Match(left, right, settings: Settings(StructureOnly, Exact)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A G H I J | false | `65.71428571428571` | 9 | listed below |
| B K N | false | `97.14285714285714` | 1 | TypeMismatch `$[0].radius` |
| C L O | false | `95.38461538461539` | 1 | Missing `$[0]` |
| D | false | `31.428571428571427` | 21 | the nine of A plus a ValueMismatch for each of the eleven identical strings and the identical null |
| E F | false | `62.857142857142854` | 10 | the nine of A plus ValueMismatch `$[5].type` (null against null) |
| M | false | `71.42857142857143` | 7 | TypeMismatch `$[0].radius`, Missing `$[1].w`, Missing `$[1].h`, Added `$[1].members`, Missing `$[2].members`, Added `$[2].w`, Added `$[2].h` |

The nine differences of A: TypeMismatch `$[0].radius`; ValueMismatch `$[1].type`, Missing `$[1].w`, Missing `$[1].h`, Added `$[1].members`; ValueMismatch `$[2].type`, Missing `$[2].members`, Added `$[2].w`, Added `$[2].h`.

Strict compares the swapped `rect` and `group` objects with each other and reports every property they do not share. Unordered recognises the swap and leaves only the real change, `1` against `"1"`. StructureOnly reports the same change differently: the shape "object with a string `type` and a number `radius`" exists only on the left, so one whole element is `Missing`; the right side's two circles with a string radius collapse into one shape that already pairs with the left `Circle`. Note how a single changed value appears as a `TypeMismatch` under B but as a `Missing` element under C: shape mode has no partial credit inside an element.

### Example 17: a realistic nested array of records

Section `core.customer.addresses`.

```json
[
  {
    "id": "addr-1",
    "type": "billing",
    "street": "Storgatan 12",
    "city": "Göteborg",
    "geo": { "lat": 57.7089, "lng": 11.9746 },
    "primary": true
  },
  {
    "id": "addr-2",
    "type": "shipping",
    "street": "Hamngatan 4",
    "city": "Malmö",
    "geo": { "lat": 55.605, "lng": 13.0038 },
    "primary": false,
    "instructions": null
  }
]
```

```json
[
  {
    "id": "addr-2",
    "type": "shipping",
    "street": "Hamngatan 4",
    "city": "Malmö",
    "geo": [55.605, 13.0038],
    "primary": false
  },
  {
    "id": "addr-1",
    "type": "billing",
    "street": "Storgatan 12",
    "city": "Göteborg",
    "geo": { "lat": 57.7089, "lng": 11.9746, "alt": 12 },
    "primary": true
  },
  {
    "id": "addr-3",
    "type": "pickup",
    "city": "Lund"
  }
]
```

```csharp
var left = Section(leftJson, "core", "customer", "addresses");
var right = Section(rightJson, "core", "customer", "addresses");
Show("A", JJMatcher.Match(left, right));
Show("B", JJMatcher.Match(left, right, settings: Settings(Unordered, Exact)));
Show("I", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Fuzzy)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A D E G H | false | `18.181818181818183` | 16 | 12 ValueMismatch, TypeMismatch `$[0].geo`, Added `$[1].geo.alt`, Missing `$[1].instructions`, Added `$[2]` |
| F I J | false | `27.272727272727273` | 14 | as A, without the ValueMismatch at `$[0].id` and `$[1].id` |
| B K | false | `72.72727272727273` | 4 | Added `$[1].geo.alt`, TypeMismatch `$[1].geo` (right `$[0].geo`), Missing `$[1].instructions`, Added `$[2]` |
| C L O | false | `74.4186046511628` | 4 | TypeMismatch `$[0].geo`, Added `$[1].geo.alt`, Missing `$[1].instructions`, Added `$[2]` |
| M N | false | `72.72727272727273` | 4 | the same four locations as C |

The two addresses swapped places, one `geo` became an array, one gained `alt`, one lost `instructions`, and a third address appeared. Under Strict every field of the swapped records is compared crosswise. Unordered pairs each address with its real counterpart and reports exactly the four changes; the `TypeMismatch` difference shows `LeftPath = $[1].geo` and `RightPath = $[0].geo`.

Rows F, I and J are a warning: Fuzzy accepts `"addr-1"` for `"addr-2"` (one edit in six characters), so two identifiers that genuinely differ stop being reported. Fuzzy matching is for free text, not for identifiers.

## 5. Value rules

### Example 18: numbers written differently

Section `core.numericValues.amounts`.

```json
{ "values": [1e2, 2.50, -0.0] }
```

```json
{ "values": [100, 2.5, 0] }
```

```csharp
var left = Section(leftJson, "core", "numericValues", "amounts");
var right = Section(rightJson, "core", "numericValues", "amounts");
Show("A", JJMatcher.Match(left, right));
Show("G", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Numeric)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B E F H I | false | `40` | 3 | ValueMismatch `$.values[0]`, `$.values[1]`, `$.values[2]` |
| C D G J K L M N O | true | `100` | 0 | |

Exact compares numbers as written, so `1e2` is not `100` and `-0.0` is not `0`. Numeric compares the quantity, exactly and without tolerance; all three pairs are equal. Here Numeric alone (D) is enough because every primitive is a number. StructureOnly (C) is also true, but for a different reason: it ignores all values below an array. Normalized and Fuzzy never look at numbers.

### Example 19: numeric precision and escaped strings

Section `core.primitiveValues`.

```json
[123456789012345678901234567890, 1.5E+10, 1.0, "tab\there \"quoted\" back\\slash café 🚀"]
```

```json
[123456789012345678901234567891, 15000000000, 1, "tab\there \"quoted\" back\\slash café 🚀"]
```

```csharp
var left = Section(leftJson, "core", "primitiveValues");
var right = Section(rightJson, "core", "primitiveValues");
Show("A", JJMatcher.Match(left, right));
Show("G", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Numeric)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B E F H I | false | `40` | 3 | ValueMismatch `$[0]`, `$[1]`, `$[2]` |
| G J K | false | `80` | 1 | ValueMismatch `$[0]` |
| D | false | `60` | 2 | ValueMismatch `$[0]`, ValueMismatch `$[3]` |
| C L M N O | true | `100` | 0 | |

The 30-digit integers differ in their last digit. A `double` could not tell them apart, but Numeric compares digits as text and keeps them distinct. `1.5E+10` equals `15000000000` and `1.0` equals `1`. The string is identical on both sides although `Left.json` writes it with JSON escapes: Exact compares decoded text, so escaping is never a difference. Numeric alone (D) rejects that identical string.

### Example 20: every numeric spelling in the fixtures

Section `core.numericValues`. The left and right values of each property denote either one quantity spelled two ways or two quantities that a floating-point type cannot tell apart.

```json
{
  "integerDecimal": 123,
  "scientific": 1e2,
  "signedZero": -0,
  "exponentDecimal": 1E+3,
  "trailingZeros": 1.5,
  "fraction": 0.1,
  "beyondDouble": 9007199254740992,
  "hugeExponent": 1e100,
  "tiny": 1e-100,
  "outsideDouble": 1e400,
  "outsideDecimal": 79228162514264337593543950336,
  "extremeExponent": 1e99999999999999999999,
  "extremeNegativeExponent": 1e-99999999999999999999,
  "negative": -1.5e3,
  "zeroForms": 0.0,
  "amounts": { "values": [1e2, 2.50, -0.0] }
}
```

```json
{
  "integerDecimal": 123.0,
  "scientific": 100,
  "signedZero": 0,
  "exponentDecimal": 1000.000,
  "trailingZeros": 1.50,
  "fraction": 0.10,
  "beyondDouble": 9007199254740993,
  "hugeExponent": 1e101,
  "tiny": 0,
  "outsideDouble": 10E399,
  "outsideDecimal": 7.9228162514264337593543950336E+28,
  "extremeExponent": 0.1e100000000000000000000,
  "extremeNegativeExponent": 10e-100000000000000000000,
  "negative": -1500,
  "zeroForms": -0.00e+5,
  "amounts": { "values": [100, 2.5, 0] }
}
```

```csharp
var left = Section(leftJson, "core", "numericValues");
var right = Section(rightJson, "core", "numericValues");
Show("A", JJMatcher.Match(left, right));
Show("G", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Numeric)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B E F H I | false | `14.285714285714286` | 18 | a ValueMismatch for all 18 numbers |
| C | false | `21.05263157894737` | 15 | the 15 numbers outside `amounts.values` |
| D G J K | false | `85.71428571428571` | 3 | ValueMismatch `$.beyondDouble`, `$.hugeExponent`, `$.tiny` |
| L | false | `84.21052631578948` | 3 | the same three as G |
| M N O | true | `100` | 0 | |

Numeric accepts every pair that denotes one quantity, including `1e400` against `10E399` (beyond `double`), the 29-digit value against its scientific spelling (beyond `decimal`) and exponents far beyond a 64-bit integer. It rejects the three pairs that really differ: 2^53 and its successor, `1e100` against `1e101`, and `1e-100` against `0`. There is no tolerance. Each side has 21 nodes; under A only the root, `amounts` and `values` match. Under C the three-element array collapses to one number shape, so its values stop counting, and the score moves to `200 × 4 / 38`.

### Example 21: normalized strings

Section `core.normalizedValues`. Each property pairs a value with a variant on the other side; `Left.json` is shown first.

```json
{
  "casing": "Masoud",
  "padding": "  Hello  ",
  "composed": "café",
  "innerSpace": "hello world",
  "punctuated": "Hello!",
  "emptyText": "",
  "blankText": "   ",
  "numberText": "123",
  "numberValue": 123,
  "booleanText": "true",
  "booleanValue": true,
  "nullText": "null",
  "nullValue": null,
  "turkish": "istanbul",
  "dottedCapital": "İstanbul",
  "dotlessSmall": "ıstanbul",
  "lonePadded": "x\uD800y",
  "loneComposed": "café\uD800",
  "repeatedWords": ["Yes", "YES", " yes "],
  "deep": { "deeper": { "greeting": "Zoë Åkesson" } }
}
```

```json
{
  "casing": "masoud",
  "padding": "hello",
  "composed": "café",
  "innerSpace": "helloworld",
  "punctuated": "hello",
  "emptyText": "",
  "blankText": "\t \n",
  "numberText": "123",
  "numberValue": 123,
  "booleanText": "true",
  "booleanValue": true,
  "nullText": "null",
  "nullValue": null,
  "turkish": "ISTANBUL",
  "dottedCapital": "istanbul",
  "dotlessSmall": "ISTANBUL",
  "lonePadded": " X\uD800Y ",
  "loneComposed": "café\uD800",
  "repeatedWords": ["yes", "yes", "yes"],
  "deep": { "deeper": { "greeting": " ZOË ÅKESSON" } }
}
```

The two `composed` values look the same but the left one is precomposed `é` and the right one is `e` followed by a combining accent; the `loneComposed` pair differs the same way and additionally ends in a lone surrogate.

```csharp
var left = Section(leftJson, "core", "normalizedValues");
var right = Section(rightJson, "core", "normalizedValues");
Show("A", JJMatcher.Match(left, right));
Show("H", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Normalized)));
Show("I", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Fuzzy)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B G | false | `42.30769230769231` | 15 | every string pair that is not identical, see the table below |
| C | false | `50` | 12 | as A without the three `repeatedWords` elements |
| D | false | `19.23076923076923` | 21 | every string, `booleanValue` and `nullValue` |
| E | false | `69.23076923076923` | 8 | `innerSpace`, `punctuated`, `numberValue`, `booleanValue`, `nullValue`, `dottedCapital`, `dotlessSmall`, `loneComposed` |
| F | false | `84.61538461538461` | 4 | `numberValue`, `booleanValue`, `nullValue`, `loneComposed` |
| H | false | `80.76923076923077` | 5 | `innerSpace`, `punctuated`, `dottedCapital`, `dotlessSmall`, `loneComposed` |
| I J K | false | `96.15384615384616` | 1 | ValueMismatch `$.loneComposed` |
| L | false | `95.83333333333333` | 1 | ValueMismatch `$.loneComposed` |
| M N O | true | `100` | 0 | |

Which rule accepts which pair, read from the rows above:

| Property | Exact | Normalized | Fuzzy | Why |
| --- | --- | --- | --- | --- |
| `casing`, `turkish` | no | yes | yes | case is ignored, independently of the current culture |
| `padding`, `blankText`, `repeatedWords[*]`, `deep.deeper.greeting` | no | yes | yes | leading and trailing white space is trimmed; a blank string equals an empty one |
| `composed` | no | yes | yes | both texts are put into Unicode NFC |
| `innerSpace`, `punctuated` | no | no | yes | internal spaces and punctuation are kept by Normalized; Fuzzy allows one edit per five characters |
| `dottedCapital`, `dotlessSmall` | no | no | yes | `İ` and `ı` are letters of their own under invariant case mapping; Fuzzy tolerates the one-letter difference |
| `lonePadded` | no | yes | yes | a lone surrogate is compared as it is; trimming and case still apply |
| `loneComposed` | no | no | no | a lone surrogate disables NFC for the whole string, so the accent forms stay different |
| `emptyText`, `numberText`, `booleanText`, `nullText` | yes | yes | yes | identical strings |
| `numberValue`, `booleanValue`, `nullValue` | yes | no | no | not strings; only Exact (or Numeric for the number) decides them |

Exact | Normalized (H) is the practical setting for case- and whitespace-insensitive comparison. Adding Fuzzy (I) accepts four more pairs that are one typo apart, which may or may not be what you want.

### Example 22: fuzzy strings

Section `core.fuzzyValues`.

```json
{
  "typo": "Jon Smith",
  "transposed": "recieve",
  "caseAndPadding": "  jon smith ",
  "boundary": "Smith",
  "shortName": "Jon",
  "twoEdits": "Jonathon Smyth",
  "swappedPairs": "Jhon Smiht",
  "wordOrder": "John Smith",
  "unrelated": "apple",
  "composedForms": "Hélène",
  "accented": "Hélène",
  "digits": "SKU-10000",
  "manyEdits": "Johnny Smith",
  "people": { "names": ["Jon Smith", "Jane Doe"] }
}
```

```json
{
  "typo": "John Smith",
  "transposed": "receive",
  "caseAndPadding": "John Smith",
  "boundary": "Smyth",
  "shortName": "John",
  "twoEdits": "Jonathan Smith",
  "swappedPairs": "John Smith",
  "wordOrder": "Smith John",
  "unrelated": "orange",
  "composedForms": "Hélène",
  "accented": "Helene",
  "digits": "SKU-10001",
  "manyEdits": "Jon Smyth",
  "people": { "names": ["John Smith", "Jane Do"] }
}
```

```csharp
var left = Section(leftJson, "core", "fuzzyValues");
var right = Section(rightJson, "core", "fuzzyValues");
Show("A", JJMatcher.Match(left, right));
Show("I", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Fuzzy)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B D G | false | `16.666666666666668` | 15 | every string pair |
| E H | false | `22.22222222222222` | 14 | every pair except `composedForms` |
| C | false | `23.529411764705884` | 13 | as A without the two `people.names` elements |
| F I J K | false | `72.22222222222223` | 5 | ValueMismatch `$.shortName`, `$.wordOrder`, `$.unrelated`, `$.accented`, `$.manyEdits` |
| L | false | `70.58823529411765` | 5 | the same five as I |
| M N O | true | `100` | 0 | |

Fuzzy folds both strings (trim, NFC, upper case) and then allows one edit, an insertion, deletion, replacement or adjacent swap, for every five UTF-16 units of the longer string.

| Property | Longer length | Allowed edits | Edits needed | Fuzzy |
| --- | ---: | ---: | ---: | --- |
| `typo` Jon Smith / John Smith | 10 | 2 | 1 | yes |
| `transposed` recieve / receive | 7 | 1 | 1 (one swap) | yes |
| `caseAndPadding` | 10 | 2 | 1 after folding | yes |
| `boundary` Smith / Smyth | 5 | 1 | 1 | yes, exactly at the limit |
| `shortName` Jon / John | 4 | 0 | 1 | no |
| `twoEdits` | 14 | 2 | 2 | yes |
| `swappedPairs` Jhon Smiht / John Smith | 10 | 2 | 2 (two swaps) | yes |
| `wordOrder` John Smith / Smith John | 10 | 2 | 10 | no |
| `unrelated` apple / orange | 6 | 1 | 5 | no |
| `composedForms` | 6 | 1 | 0 after NFC | yes; Normalized accepts it too |
| `accented` Hélène / Helene | 6 | 1 | 2 | no: accents are not removed |
| `digits` SKU-10000 / SKU-10001 | 9 | 1 | 1 | yes, which is rarely wanted for identifiers |
| `manyEdits` Johnny Smith / Jon Smyth | 12 | 2 | 4 | no |
| `people.names[0]`, `people.names[1]` | 10, 8 | 2, 1 | 1, 1 | yes |

Short strings tolerate no edit at all, word order is never fuzzy, and a changed digit is accepted like any other character. Fuzzy acceptance is all or nothing: an accepted pair earns the full node, a rejected one earns nothing.

### Example 23: fuzzy values inside unordered arrays

Section `publicApi.fuzzyAssignment`.

```json
["Jhon Smiht", "Jon Smith"]
```

```json
["Jon Smyth", "Jon Smith"]
```

```csharp
var left = Section(leftJson, "publicApi", "fuzzyAssignment");
var right = Section(rightJson, "publicApi", "fuzzyAssignment");
Show("I", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Fuzzy)));
Show("K", JJMatcher.Match(left, right, settings: Settings(Unordered, Exact | Fuzzy)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B E F G H I J | false | `66.66666666666667` | 1 | ValueMismatch `$[0]` |
| C K L M N O | true | `100` | 0 | |
| D | false | `33.333333333333336` | 2 | ValueMismatch `$[0]`, `$[1]` |

With Strict arrays and Fuzzy, `"Jhon Smiht"` is compared with `"Jon Smyth"`: three edits in ten characters, one too many. With Unordered arrays and Fuzzy (K), the assignment pairs `"Jhon Smiht"` with `"Jon Smith"` (two swaps) and `"Jon Smith"` with `"Jon Smyth"` (one replacement), and both pairs are accepted. Unordered + Exact (B) is still false because only the identical `"Jon Smith"` pair is exact. The array policy flipped `IsMatch` without any value changing.

This also shows that fuzzy similarity is not transitive: `"Jhon Smiht"` matches `"Jon Smith"`, and `"Jon Smith"` matches `"Jon Smyth"`, but `"Jhon Smiht"` does not match `"Jon Smyth"`.

### Example 24: Unicode escapes and normalization forms

Section `stress.strings.hexCaseVariants`.

```json
["é", "é", "é"]
```

```json
["é", "é", "é"]
```

```csharp
var left = Section(leftJson, "stress", "strings", "hexCaseVariants");
var right = Section(rightJson, "stress", "strings", "hexCaseVariants");
Show("A", JJMatcher.Match(left, right));
Show("H", JJMatcher.Match(left, right, settings: Settings(Strict, Exact | Normalized)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B G | false | `75` | 1 | ValueMismatch `$[2]` |
| C E F H I J K L M N O | true | `100` | 0 | |
| D | false | `25` | 3 | ValueMismatch `$[0]`, `$[1]`, `$[2]` |

The first two pairs are the same character written as a lower-case escape, an upper-case escape and a literal; Exact decodes the JSON text before comparing, so they are equal. The third pair is `é` against `e` plus a combining acute accent: different code points, so Exact rejects them and Normalized, which applies NFC, accepts them. StructureOnly (C) is true because all three elements are the one string shape and values below arrays are ignored.

Property **names** are never normalized or case-folded. The section `core.unicodeNormalization`, `{ "café": 1, "café": 2 }` on both sides, holds two distinct properties and is a complete match under A; the names stay distinct under Exact | Normalized too.

### Example 25: strings that no rule will ever accept

Some pairs look alike to a human and never match in JJMatch. Each row is a single root value, so the score is `0` or `100`.

| Section | Left | Right | A to L | M, N, O |
| --- | --- | --- | --- | --- |
| `stress.strings.homoglyphs[0]` vs `[1]` | `"paypal"` | `"pаypаl"` (Cyrillic а) | false, `0`, ValueMismatch `$` | true, `100` |
| `stress.strings.homoglyphs[0]` vs `[2]` | `"paypal"` | `"ｐａｙｐａｌ"` (fullwidth) | false, `0`, ValueMismatch `$` | true, `100` |
| `stress.strings.commentLookalikes` | `"// not a comment /* still not */ # nope"` | `"not a comment"` | false, `0`, ValueMismatch `$` | true, `100` |
| `stress.numbers.int32Max` | `2147483647` | `"2147483647"` | false, `0`, TypeMismatch `$` | false, `0`, TypeMismatch `$` |
| `core.customer.größe` | `172.5` | `"172.5 cm"` | false, `0`, TypeMismatch `$` | false, `0`, TypeMismatch `$` |

```csharp
Show("I", JJMatcher.Match(Section(leftJson, "stress", "strings", "homoglyphs", 0),
    Section(rightJson, "stress", "strings", "homoglyphs", 1), settings: Settings(Strict, Exact | Fuzzy)));
```

NFC does not fold Cyrillic or fullwidth letters onto Latin ones, and two substitutions in six characters exceed the fuzzy allowance, so the homoglyph pairs are rejected by every rule. A substring relationship is not a rule either. A number against its own digits in a string is a type mismatch everywhere, including under structural matching.

## 6. Objects, names and duplicates

### Example 26: duplicate property names

JSON allows a name to appear twice in one object. JJMatch keeps every occurrence and pairs occurrences of the same name one-to-one. Three sections show the rules.

**`publicApi.duplicates`**

```json
{ "entry": { "value": 1 }, "entry": { "value": 2 } }
```

```json
{ "entry": { "value": 2 }, "entry": { "value": 3 } }
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C D G H I J K L | false | `80` | 1 | ValueMismatch `$.entry#1.value` (right `$.entry#2.value`) |
| E F | false | `60` | 2 | ValueMismatch `$.entry#1.value`, `$.entry#2.value` |
| M N O | true | `100` | 0 | |

The `#1` and `#2` suffixes are one-based occurrence numbers. The two `value: 2` occurrences pair with each other, and the difference links the first left occurrence to the second right one. Five nodes a side, four matched.

**`stress.duplicates.simple`**

```json
{ "k": 1, "k": 2, "k": 3 }
```

```json
{ "k": 3, "k": 2, "k": 1 }
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C D G H I J K L M N O | true | `100` | 0 | |
| E F | false | `25` | 3 | ValueMismatch `$.k#1`, `$.k#2`, `$.k#3` |

The order of duplicates does not matter, in any array mode: the group is assigned as a whole. A consumer that keeps the last occurrence would read `3` on one side and `1` on the other; JJMatch reports no difference.

**`stress.duplicates.interleaved`**

```json
{ "a": 1, "b": 2, "a": 3, "b": 4, "a": 5 }
```

```json
{ "a": 5, "b": 4 }
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C D G H I J K L | false | `66.66666666666667` | 3 | Missing `$.a#1`, Missing `$.b#1`, Missing `$.a#2` |
| E F | false | `22.22222222222222` | 5 | ValueMismatch `$.a#1`, ValueMismatch `$.b#1`, Missing `$.a#2`, Missing `$.b#2`, Missing `$.a#3` |
| M N O | false | `66.66666666666667` | 3 | Missing `$.a#2`, Missing `$.b#2`, Missing `$.a#3` |

Six nodes against three. With values compared, the pairing chooses the occurrences that match: `a: 5` and `b: 4`, so the first and second `a` and the first `b` are surplus. With values ignored every pairing matches the same nodes, and the tie is resolved in document order, so the **last** occurrences are reported instead. The score is the same because the number of matched nodes is.

```csharp
var left = Section(leftJson, "stress", "duplicates", "interleaved");
var right = Section(rightJson, "stress", "duplicates", "interleaved");
Show("A", JJMatcher.Match(left, right));
Show("M", JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching));
```

### Example 27: duplicates of different types

Section `core.duplicateTypeOrder`.

```json
{ "value": 123, "value": "hello", "retyped": { "value": true, "value": "hello" } }
```

```json
{ "value": "world", "value": 456, "retyped": { "value": "hello", "value": false } }
```

```csharp
var left = Section(leftJson, "core", "duplicateTypeOrder");
var right = Section(rightJson, "core", "duplicateTypeOrder");
Show("A", JJMatcher.Match(left, right));
Show("M", JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C E F G H I J K L | false | `50` | 3 | TypeMismatch `$.value#1`, TypeMismatch `$.value#2`, ValueMismatch `$.retyped.value#1` (right `$.retyped.value#2`) |
| D | false | `33.333333333333336` | 4 | TypeMismatch `$.value#1`, `$.value#2`, `$.retyped.value#1`, `$.retyped.value#2` |
| M N O | true | `100` | 0 | |

Inside `retyped` the pairing crosses occurrences so that `"hello"` meets `"hello"` and `true` meets `false`, a value difference between two Booleans. At the top level, `123` against `"world"` and `"hello"` against `456` both fail, and crossing them would not help: `123` is not `456` and `"hello"` is not `"world"`, so no pairing matches a node and document order decides. Under D the string pair in `retyped` cannot match (Numeric ignores strings), which removes the reason to cross, and the Booleans become type mismatches against strings. Structural matching (M) pairs by type and finds nothing wrong.

### Example 28: names with punctuation, quotes and case differences

Three tiny sections show how paths are written and that names are compared exactly.

**`core['a.b']`**, a section whose own name contains a dot:

```json
{ "c.d": 1, "e[0]": "bracketed" }
```

```json
{ "c.d": 1, "e[0]": ["bracketed"] }
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C D G H I J K L M N O | false | `57.142857142857146` | 1 | TypeMismatch `$['e[0]']` |
| E F | false | `28.571428571428573` | 2 | ValueMismatch `$['c.d']`, TypeMismatch `$['e[0]']` |

A name that is not a plain identifier is written in brackets and single quotes. `$['e[0]']` is one property named `e[0]`, not element zero of `e`.

**`core.quotedNames`**

```json
{ "owner's name": "Ali", "child": { "it's valid": true } }
```

```json
{ "owner's name": "Reza", "child": { "it's valid": false, "it\\'s": 1 } }
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C D E F G H I J K L | false | `44.44444444444444` | 3 | ValueMismatch `$['owner\'s name']`, ValueMismatch `$.child['it\'s valid']`, Added `$.child['it\\\'s']` |
| M N O | false | `88.88888888888889` | 1 | Added `$.child['it\\\'s']` |

Quotes and backslashes inside a name are escaped with a backslash, so a name can never close the brackets early. The `LeftValue` of the first difference is the string `Ali`.

**`stress.containers.almostSameShape[0]` vs `[4]`**, two elements of one array:

```json
{ "a": 1, "b": 2 }
```

```json
{ "a": 1, "B": 2 }
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C D G H I J K L M N O | false | `66.66666666666667` | 2 | Missing `$.b`, Added `$.B` |
| E F | false | `33.333333333333336` | 3 | ValueMismatch `$.a`, Missing `$.b`, Added `$.B` |

Property names are case-sensitive under every configuration. Normalized relaxes string **values**, never names.

```csharp
Show("A", JJMatcher.Match(Section(leftJson, "core", "a.b"), Section(rightJson, "core", "a.b")));
Show("A", JJMatcher.Match(Section(leftJson, "stress", "containers", "almostSameShape", 0),
    Section(rightJson, "stress", "containers", "almostSameShape", 4)));
```

### Example 29: invisible characters in names

Section `core.pathEscapes`. The names contain control characters, zero-width and no-break spaces, a byte order mark, a line separator, a supplementary letter, a tag character and a name that starts with an Arabic-Indic digit.

```json
{
  "back\bspace\fform": 1,
  "nul\u0000unit\u001f": 2,
  "del\u007f": 3,
  "nel\u0085": 4,
  "zero​width": 5,
  "line sep": { "1st": 6 },
  "no break": 7,
  "﻿bom": 8,
  "𐐀plain": 9,
  "tag󠀁": 10,
  "٣rd": 11,
  "_": { "del\u007f": 12 }
}
```

```json
{
  "back\bspace\fform": 1,
  "nul\u0000unit\u001f": 2,
  "del\u007f": 3,
  "nel\u0085": 4,
  "zero​width": 5,
  "line sep": { "1st": 6, "2nd": 7 },
  "no break": 8,
  "﻿bom": 9,
  "𐐀plain": 10,
  "tag󠀁": 11,
  "٣rd": 12,
  "_": { "del\u007f": "13" },
  "\u001b[0m": 14,
  "para graph": 15
}
```

```csharp
var left = Section(leftJson, "core", "pathEscapes");
var right = Section(rightJson, "core", "pathEscapes");
foreach (var difference in JJMatcher.Match(left, right).Differences)
    Console.WriteLine($"{difference.Kind} {difference.Path}");
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C D G H I J K L | false | `54.54545454545455` | 9 | listed below |
| E F | false | `18.181818181818183` | 15 | the nine below plus a ValueMismatch for each of the six identical numbers |
| M N O | false | `84.84848484848484` | 4 | Added `$['line sep']['2nd']`, TypeMismatch `$._['del\u007F']`, Added `$['\u001B[0m']`, Added `$['para graph']` |

The nine paths printed by the loop above, exactly as returned:

```text
Added $['line sep']['2nd']
ValueMismatch $['no break']
ValueMismatch $['﻿bom']
ValueMismatch $.𐐀plain
ValueMismatch $['tag󠀁']
ValueMismatch $['٣rd']
TypeMismatch $._['del\u007F']
Added $['\u001B[0m']
Added $['para graph']
```

Every character that would not show on screen is written as a JSON escape inside the brackets, so a path is always one printable line and two names that look alike stay distinct. Visible characters, including the Arabic-Indic digit, are written as they are; a name that starts with a digit is quoted. The Deseret letter `𐐀` is a letter, so `𐐀plain` is a plain name. The names on the difference snapshots are never changed; only the `Path` text is escaped. Paths are diagnostics, not executable JSONPath queries.

### Example 30: nested objects with repeated names and an empty name

Section `core.meta`.

```json
{
  "id": "meta-1",
  "source": "web",
  "flags": { "gift": false, "priority": true, "données": { "clé": "valeur" } },
  "meta": {
    "meta": { "depth": 3, "name": "innermost", "": "nested empty property name" }
  }
}
```

```json
{
  "id": "meta-1",
  "source": "mobile",
  "flags": { "gift": false, "données": { "clé": "valeur", "schlüssel": "wert" } },
  "meta": {
    "meta": { "depth": 3, "": "nested empty property name" }
  }
}
```

```csharp
var left = Section(leftJson, "core", "meta");
var right = Section(rightJson, "core", "meta");
Show("A", JJMatcher.Match(left, right));
Show("D", JJMatcher.Match(left, right, settings: Settings(Strict, Numeric)));
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A B C G H I J K L | false | `80` | 4 | ValueMismatch `$.source`, Missing `$.flags.priority`, Added `$.flags.données.schlüssel`, Missing `$.meta.meta.name` |
| D | false | `48` | 8 | the four above plus ValueMismatch `$.id`, `$.flags.gift`, `$.flags.données.clé`, `$.meta.meta['']` |
| E F | false | `64` | 6 | the four above plus ValueMismatch `$.flags.gift`, `$.meta.meta.depth` |
| M N O | false | `88` | 3 | Missing `$.flags.priority`, Added `$.flags.données.schlüssel`, Missing `$.meta.meta.name` |

Thirteen nodes against twelve; ten match under A. A property named `meta` inside `meta` inside the root is simply `$.meta.meta`; names are paired level by level, so repetition at different depths is not special. Accented names such as `données` and `schlüssel` are plain identifiers. The empty name is written `['']`, visible in row D. Rows D, E and F show the by-now familiar effect of a relaxed rule used alone.

## 7. Deep documents, stringified input and limits

### Example 31: one difference deep inside a large document

The `stress.deepObject` section nests the property `a` 30 times on the left and 25 times on the right around the string `"30 levels down"`. The `stress.deepArray` section nests 100 arrays around `"100 levels down"` on the left and `"100 levels dwon"` on the right. They are not reproduced here.

```csharp
var deep = new JJMatchSettings { MaxDepth = 128 }; // the default of 64 rejects a 100-level array
Show("A", JJMatcher.Match(Section(leftJson, "stress", "deepObject"), Section(rightJson, "stress", "deepObject")));
Show("A", JJMatcher.Match(Section(leftJson, "stress", "deepArray"), Section(rightJson, "stress", "deepArray"), settings: deep));
Show("I", JJMatcher.Match(Section(leftJson, "stress", "deepArray"), Section(rightJson, "stress", "deepArray"),
    settings: new JJMatchSettings { MaxDepth = 128, ValueMode = Exact | Fuzzy }));
```

| Section | Configurations | IsMatch | Score | Count | Differences |
| --- | --- | --- | ---: | ---: | --- |
| `deepObject` | all fifteen | false | `87.71929824561404` | 1 | TypeMismatch at `$.a` repeated 25 times |
| `deepArray`, MaxDepth 128 | A B D E G H | false | `99.00990099009901` | 1 | ValueMismatch at `$[0]` repeated 100 times |
| `deepArray`, MaxDepth 128 | C F I J K L M N O | true | `100` | 0 | |
| `deepArray`, default MaxDepth | any | throws `JsonException` | | | |

In `deepObject` the 25th-level object on the left meets a string on the right: one `TypeMismatch`, yet six nodes are unmatched, `200 × 25 / 57`. In `deepArray` one typo among 101 nodes a side costs less than one point, and Fuzzy repairs it ("dwon" is one swap away from "down"; C ignores the value because it sits inside an array). One difference can be cheap or expensive; the score counts nodes, not diagnostics.

### Example 32: stringified JSON input

Some APIs return a whole JSON document encoded inside a JSON string. The fixture `tests/JJMatch.Tests/TestData/Stringified/LeftStringify.txt` is exactly that: the text of `Left.json` serialized as one JSON string, and `RightStringify.txt` likewise for `Right.json`. Such input is recognized and decoded without any setting. Both whole fixtures contain the 100-level array, so every row needs `MaxDepth = 128`.

```csharp
string leftText = File.ReadAllText("tests/JJMatch.Tests/TestData/Stringified/LeftStringify.txt");
string rightText = File.ReadAllText("tests/JJMatch.Tests/TestData/Stringified/RightStringify.txt");
var deep = new JJMatchSettings { MaxDepth = 128 };
Show("stringified vs plain", JJMatcher.Match(leftText, leftJson, settings: deep));
Show("stringified L vs R", JJMatcher.Match(leftText, rightText, settings: deep));
```

| Left | Right | Settings | IsMatch | Score | Count | Differences |
| --- | --- | --- | --- | ---: | ---: | --- |
| `LeftStringify.txt` | `Left.json` | default | throws `JsonException`: depth 64 exceeded | | | |
| `LeftStringify.txt` | `Left.json` | MaxDepth 128 | true | `100` | 0 | |
| `Left.json` | `LeftStringify.txt` | MaxDepth 128 | true | `100` | 0 | |
| `LeftStringify.txt` | `RightStringify.txt` | MaxDepth 128 | false | `70.7037643207856` | 314 | 151 ValueMismatch, 44 TypeMismatch, 43 Missing, 76 Added |
| `Left.json` | `Right.json` | MaxDepth 128 | false | `70.7037643207856` | 314 | the same 314 |
| `LeftStringify.txt` | `RightStringify.txt` | MaxDepth 128, Unordered, all four rules | false | `82.74959083469722` | 179 | 45 ValueMismatch, 25 TypeMismatch, 40 Missing, 69 Added |

A root string is decoded exactly once on each side, and only when its content is a JSON object or array; an ordinary object or array root is untouched, which is why mixing a stringified and a plain input works. Decoding is not normalization: the decoded fixtures give the same 314 differences as the files themselves.

Strings nested **inside** a document stay literal. Section `publicApi.nestedStringified`:

```json
{ "payload": "{\"a\":1}" }
```

```json
{ "payload": "{ \"a\": 1 }" }
```

| Configurations | IsMatch | Score | Count | Differences |
| --- | --- | ---: | ---: | --- |
| A to L | false | `50` | 1 | ValueMismatch `$.payload` |
| M N O | true | `100` | 0 | |

The two payloads differ by internal spaces, which Normalized keeps and Fuzzy counts as three edits in eleven characters, so no value rule accepts them, and no setting parses them as JSON.

Two further rows from the fixtures: `publicApi.invalidText` is the string `"{"` on the left and `"not json"` on the right. Neither decodes as a JSON object or array, so both stay literal strings: the pair is one `ValueMismatch` at `$`, and each against itself is a match, while passing the content `{` directly as an input throws `JsonException`. `core.loneSurrogates.values[0]` is the string `"\uD800"`, a lone surrogate, which cannot be decoded as text; it stays a literal string too, a match against itself and a `TypeMismatch` at `$` against `Left.json`.

### Example 33: resource limits, invalid options and errors

The limits below were checked against sections shown earlier. Each row changes only the stated settings.

| Inputs | Settings | Outcome |
| --- | --- | --- |
| `core.payment` (Example 6; 26 nodes in total, depth 3, 247 and 289 characters) | `MaxNodes = 26` | normal result, as Example 6 |
| same | `MaxNodes = 25` | `InvalidOperationException`: Comparison exceeded JJMatchSettings.MaxNodes. |
| same | `MaxDepth = 3` | normal result |
| same | `MaxDepth = 2` | `JsonException`: the maximum configured depth of 2 has been exceeded |
| same | `MaxWork = 1` | `InvalidOperationException`: Comparison exceeded JJMatchSettings.MaxWork. |
| same | `MaxInputLength = 100` | `InvalidOperationException`: Input exceeded JJMatchSettings.MaxInputLength. |
| `core` (the whole core section, 1181 nodes in total, depth 8) | default | false, `61.30397967823878`, 206 differences |
| same | `MaxDepth = 7` | `JsonException` |
| `core.assignmentLimit` (33 distinct element shapes a side) | Unordered, `MaxAssignmentSize = 33` | false, `87.54716981132076`, 33 differences |
| same | Unordered, `MaxAssignmentSize = 32` | `InvalidOperationException`: Comparison exceeded JJMatchSettings.MaxAssignmentSize. Use Strict arrays or increase the limit. |
| same | Strict, `MaxAssignmentSize = 1` | false, `87.54716981132076`, 33 differences: Strict arrays never assign |
| `publicApi.arrayValues` (Example 8) | Unordered, `MaxAssignmentSize = 2` | `InvalidOperationException` for MaxAssignmentSize |
| `stress.duplicates.simple` (Example 26, three `k` occurrences) | default arrays, `MaxAssignmentSize = 2` | `InvalidOperationException` for MaxAssignmentSize: duplicate names are assigned too |

Defaults: 1,000,000 UTF-16 characters per input, depth 64, 100,000 total nodes, 128 occurrences per side in an unordered or duplicate-name group, and a work budget of 10,000,000. Depth can be raised to 128, nodes to 1,000,000 and group size to 256. A limit failure throws; it never returns an approximate score.

Options and value rules that are invalid or simply redundant, all checked on `publicApi.basic`:

| Call | Outcome |
| --- | --- |
| `ValueMode = (JJValueMatchingMode)16`, an undefined bit | `ArgumentOutOfRangeException` |
| `ValueMode = None` | `ArgumentException`: Select a value rule, or use StructuralMatching to ignore values. |
| `ValueMode = None` with `JJMatchOptions.StructuralMatching` | allowed: true, `100`, 0 |
| `(JJMatchOptions)64`, an undefined bit | `ArgumentOutOfRangeException` |
| `JJMatchOptions.ValueMatching` alone | same result as the default; `StructuralMatchPercentage` is `null` |
| `MaxDepth = 129` | `ArgumentOutOfRangeException` |
| a C# `null` input | `ArgumentNullException` |
| an already cancelled `CancellationToken` | `OperationCanceledException` |

```csharp
try
{
    JJMatcher.Match(Section(leftJson, "core", "payment"), Section(rightJson, "core", "payment"),
        settings: new JJMatchSettings { MaxNodes = 25 });
}
catch (InvalidOperationException exception)
{
    Console.WriteLine(exception.Message); // Comparison exceeded JJMatchSettings.MaxNodes.
}
```

## 8. Choosing a configuration

| You want to ignore | Use |
| --- | --- |
| nothing: exact values, ordered arrays | the default |
| array element order, but not counts | `ArrayMode = Unordered` |
| array element order, counts and every value inside arrays | `ArrayMode = StructureOnly` |
| number spelling (`1.0` vs `1`, `1e2` vs `100`) | `ValueMode = Exact \| Numeric` |
| string case, outer white space and Unicode form | `ValueMode = Exact \| Normalized` |
| small typos in free text | `ValueMode = Exact \| Fuzzy`, never on identifiers |
| all primitive values, keeping names and types | `JJMatchOptions.StructuralMatching` |

Combine rules with `|`, keep `Exact` whenever the documents hold more than one primitive type, and use `IsMatch` rather than a rounded score to decide success. Property names, JSON types and the presence or absence of a property are never relaxed by any setting.

All results on this page were produced with JJMatch 0.1.0 on .NET 10. Scores are deterministic for the same inputs, settings and runtime, and swapping the two inputs preserves `IsMatch` and `Score`; Unicode normalization uses the runtime's Unicode tables.
