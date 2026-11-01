# Match

Fixture paths and counts below are relative to the `core` document unless explicitly identified as `stress`. Both sections live in the approved JSON pair; selecting them preserves the original test scenarios.

`StructureMatcher.Match` compares the structure of two `JsonStructureNode` trees built by [`Traverse`](Traverse.md) and returns what matched and every structural difference. Its original two-argument overload retains structural StructureOnly behavior. Its configured overload, used by public `JJMatcher.Match`, also supports Strict arrays and primitive value rules.

## Purpose

Decides whether two documents have exactly the same structure, and says why when they do not. The configured overload delegates primitive decisions to [`MatchValues`](MatchValues.md). The public facade parses documents, counts effective nodes, calls this engine, calculates [Dice similarity](Scoring.md), and translates selected node differences into detached public snapshots. The engine itself does not calculate percentages.

## Configured comparison

The configured overload takes ArrayMode, ValueMode, CompareValues, a per-call ComparisonBudget, an optional shared ComparisonCache and an optional PropertyNameMode. Strict arrays retain every occurrence and pair elements wherever they stand, maximizing the shared node count across the whole array; any two elements may be paired, whatever their types. Element position plays no part in any mode. StructureOnly arrays deduplicate structural shapes and disable value comparison throughout each array subtree. Outside those subtrees, enabled value rules determine whether each primitive earns one matched node or a ValueMismatch. Containers still earn one point for matching type. True/false are one structural type but differ under Exact.

PropertyNameMode decides which names label one property. Under `None`, the default, only identical names pair: names are compared ordinally as the legacy overload compares them. Under `Normalized`, a name is keyed by its `ValueMatcher.Normalize` form (trimmed, NFC) and keys are compared with `OrdinalIgnoreCase`, exactly as `Normalized` compares string values, so `name` and `Name` are one name; `postalCode` and `postal_code` are not. The rule is applied everywhere a name is read: `NameIndex` keys properties by it, so names of one object that become equal form a duplicate-name group and are assigned like any other; `CanPair` tests shared names through it; and the `ComparisonCache` interns names under it, so two objects whose names differ only as the rule allows have one identity, and under StructureOnly collapse to one element shape and count once. The rule is structural: it applies whether or not values are compared, and so to the structural diagnostic. A pair of names it accepts is a match with no difference reported; the facade reports an unpaired property under its own spelling, and occurrence suffixes still follow exact names, so `$.name` and `$.Name` stay distinct paths.

Whatever `PropertyNameMode` is configured, `CompareProperties` also looks, among the properties left over after exact-name pairing, for a left and a right property that neither side's exact spelling matches at all but that fold to the same key under `Normalized`. Such a pair is reported once as `PropertyNameCaseMismatch` instead of a `MissingProperty` and an `ExtraProperty`, with both nodes set and nothing below them compared, like a `TypeMismatch`. It never changes `MatchedCount`: the pair stays unmatched exactly as the two separate differences would have left it, only its public kind and scoring weight are lighter. Under `PropertyNameMode.Normalized` such a pair never occurs, because the exact-name pairing step already folds the two names together.

A paired primitive that the configured value rules reject is also checked, regardless of `ValueMode`, against the same `Normalized` fold `ValueMatcher` already uses for string values (trimmed, NFC, `OrdinalIgnoreCase`). Two strings that fold to equal this way are reported as one lighter `ValueCaseMismatch` instead of a full `ValueMismatch`; every other rejected pair, including one where either side is not a string, stays `ValueMismatch`. The pair was unmatched either way, so `MatchedCount` is unchanged. Under a `ValueMode` that already includes `Normalized`, such a pair never occurs, because `MatchValues` would have accepted it first.

When a normalized orphan-name group has surplus occurrences, Weighted scoring selects the occurrences to pair by their total penalty, including those left Missing or Added. On each side, pairing replaces the surplus weight by `PropertyNameCaseMismatchWeight` for every node of that subtree; selecting the lowest cost changes minimizes the total independently on each side. Equally good selections retain document order, as does pairing the selected occurrences. Standard scoring retains the original earliest-occurrence selection. No values below a case-name mismatch are compared.

`WeightedAssignmentPipelineTests` uses `publicApi.weightedAssignments` to verify surplus costs in array and duplicate-property assignments, and property-order independence of case-name surplus selection. Both input directions are covered, with Missing and Added weights swapped; case-name tests exercise surplus weights above and below the case-name weight.

`CountNodes` uses the same recursive shape reduction as comparison when ArrayMode is StructureOnly, and otherwise counts every node. The facade uses this count as the Dice denominator. Public limits bound recursion, nodes, assignment sizes and total work before expensive operations; exhaustion throws rather than producing an approximate assignment. The old unbudgeted overload remains internal for compatibility and tests.

The selected differences retain original node references. The facade maps those references to original array indices and duplicate occurrence numbers, ignoring the internal wildcard Path for this purpose. It maps missing/extra nodes to public Missing/Added, type mismatches to TypeMismatch, and rejected primitive values to ValueMismatch. Match results and snapshots survive disposal of both parsing documents.

`JJMatcherIntegrationTests` verifies the public pipeline, numerical scores, original paths, malformed inputs, resource limits, cancellation, concurrent call isolation, all policies, and score/result/difference invariants. For `PropertyNameMode` it uses `publicApi.propertyNames` (names differing in case, outer white space and Unicode form, one name that still differs, and element objects whose names fold together under StructureOnly) and `core.caseVariants` against `publicApi.basic` (three case variants as one duplicate group, with their original spellings in the paths), and runs the symmetry and determinism sweep under both name modes. For `ValueCaseMismatch` it uses `core.normalizedValues` (`casing`, `padding`, `blankText`, `turkish` and `lonePadded` fold to equal and report the lighter kind; `innerSpace`, `punctuated`, `dottedCapital`, `dotlessSmall` and `loneComposed` differ by more than case and stay a full `ValueMismatch`), and the guide's own `customer.name` pair (`"  Alice Chen  "` against `"alice chen"`) exercises it through the `guideScenario` fixture too. The following sections document the **legacy two-argument structural overload**, whose semantics and existing tests are preserved. Primitive value statements and wildcard paths there refer to that overload, not the public result.

## Legacy input and output

- `JsonStructureNode left`, `JsonStructureNode right`: the two trees. Any node can be passed as a root, so a section of a document can be compared on its own. The roots' own property names are not compared.
- Returns a `StructureMatchResult`. `StructureMatchResult.cs`, `StructureDifference.cs` and `StructureMismatchType.cs` live in this folder. All three types are internal.

| `StructureMatchResult` | Content |
| --- | --- |
| `IsMatch` | `true` only when `Differences` is empty. |
| `MatchedCount` | The number of structurally matched nodes: paired nodes of the same type, containers included. Each distinct array element shape counts once, however often it occurs. It is the maximum any pairing could reach under the pairing rules; see Pairing. |
| `Differences` | Every structural difference. |

| `StructureDifference` | Content |
| --- | --- |
| `MismatchType` | The category, see below. |
| `Path` | Where the difference is, for reporting only. |
| `Left`, `Right` | The nodes concerned, with everything below them; `null` on the side that has nothing here. |

| `StructureMismatchType` | Meaning | `Left` | `Right` |
| --- | --- | --- | --- |
| `MissingProperty` | The left object has a property name the right object does not have at all. | set | `null` |
| `ExtraProperty` | The right object has a property name the left object does not have at all. | `null` | set |
| `TypeMismatch` | Both sides have a value here, of different types. | set | set |
| `MissingArrayElementShape` | The left array holds an element shape the right array does not hold. | set | `null` |
| `ExtraArrayElementShape` | The right array holds an element shape the left array does not hold. | `null` | set |
| `DuplicatePropertyCountMismatch` | Both objects have the property name and one declares it more often. One difference per surplus occurrence, with the surplus node on its side. | set or `null` | `null` or set |
| `PropertyNameCaseMismatch` | Neither side has the other's exact property name, but the two fold to one key under Normalized. | set | set |

The facade translates these internal categories into `JJDifferenceKind`; the configured overload can additionally produce internal `ValueMismatch`, `ValueCaseMismatch` and `PropertyNameCaseMismatch`. `ValueMismatch` and `ValueCaseMismatch` are absent from the table above because they require `CompareValues`, which the legacy overload never sets; `ValueCaseMismatch` is the same rejected-pair case as `ValueMismatch`, except both primitives are strings whose normalized texts are equal ignoring case. Under the configured overload the two array-shape categories also name a surplus element of a Strict array (an element the assignment left over), whatever shape the other array holds; the facade reports it as Missing or Added at the element's original index.

## Behavior

- A full match has zero differences. Any structural difference, however small, makes `IsMatch` `false`.
- `Value` is never read, and no string is parsed here. A string holding stringified JSON is a `String` leaf like any other, unless `Traverse` already decoded it into the object or array it holds, as it does for `JJMatcher.Match` (see `Traverse.md`).
- Types are the JSON kinds, with `True` and `False` as one Boolean type. Every other kind matches only itself: a number against a string, `null` against anything else, and an object against an array are each a `TypeMismatch`.
- Nothing below a `TypeMismatch` is compared or reported. The two nodes carry their subtrees.
- Object properties are paired by name, in any order. Names are compared ordinally, without case folding or Unicode normalization, as `Traverse` stores them; the configured overload relaxes this only under `PropertyNameMode.Normalized`, described above.
- A property on one side only is one difference at the top of its subtree; its descendants are not reported separately. When the other side turns out to hold the same property under a name that folds together only under Normalized, and neither side's exact name exists on the other side at all, the two one-sided differences become one `PropertyNameCaseMismatch` instead, naming both properties; its descendants are not reported separately either.
- Properties sharing a name in one object are paired in any order too, as described under Pairing: `{ value: 123, value: "hello" }` matches `{ value: "world", value: 456 }`, the number with the number and the string with the string. As many of them as possible are paired, whatever their types: two of different types are a `TypeMismatch`, with both nodes. An occurrence left without a partner is a `DuplicatePropertyCountMismatch`: against a single `value` string, the number is the surplus wherever it stands. When the other object does not have the name at all, every occurrence is a `MissingProperty` or `ExtraProperty`.
- Every occurrence stays a node of its own. Nothing is merged or dropped, and each difference holds the exact occurrence it concerns.
- An array is never flattened: it is a node and its elements are its own children, so an object and an array holding that object are a `TypeMismatch`, and a nested array is an element shape of its parent.

### Arrays

- Each array is reduced to its distinct element shapes. Element order and how often a shape occurs play no part.
- A shape is the structure of an element under these same rules: its type, an object's property names with their shapes whatever their order, an array's distinct element shapes.
- An empty array holds no shape, so `[]` against a non-empty array reports each shape of the other side.
- The shapes are paired as described under Pairing, so that the exact difference is reported instead of two whole elements. Two primitives of one type, or two arrays, can always be paired; two objects only when they share at least one property name or are both empty. Nothing else is paired.
- Pairing never hides a difference: two shapes that are not identical always produce at least one difference when compared.
- A shape without a partner is reported whole as `MissingArrayElementShape` or `ExtraArrayElementShape`.
- A shape held by both arrays is paired with itself unless another pairing matches more nodes, and `[{a}]` against `[{a}, {a, b}]` reports `{a, b}` as one extra shape. The exception is rare and pinned by a test: `[{ north, south }, { south }]` against `[{ south, north }, { north }]` pairs crosswise, because `{ south }` and `{ north }` share no name and could not be paired otherwise.

### Pairing

Same-named properties and array element shapes are paired by one rule.

- **It never decides whether two structures match.** Two nodes of the same shape can always be paired, and pairing them matches every node on both sides. When both sides hold the same shapes, that pairing matches every node there is, and no other assignment reaches the same count: a pair of different shapes leaves at least one node unmatched. So the best assignment pairs the identical shapes, and the result has no differences. When the shapes differ, there is at least one difference however they are paired. Pairing only decides which differences are reported, and `MatchedCount`.
- **Cost.** A pair leaves unmatched the nodes of both subtrees that found no partner; an occurrence without a partner leaves all of its nodes unmatched. Nodes are counted as `MatchedCount` counts them. The pairs are chosen one to one so that the fewest nodes are left unmatched over the whole group, which is the same as matching the most nodes. A whole unmatched subtree therefore weighs its size, not one, although it is still reported as one difference. Only names, types and nesting are read, never `Value`.
- **This is the alignment objective.** It is an integer node count with no percentage. The public facade now uses the count as Dice overlap, with the denominator defined in Scoring.md.
- **The count is the maximum, always.** Every pair that may be made is compared, and the group is assigned as a whole whatever its size, so `MatchedCount` is the greatest count any pairing could reach under the rules above, for every input. Pairing identical shapes first and assigning only the rest, as earlier versions did, could lose nodes when two leftover objects shared no name, and that is why the whole group is assigned. Because the maximum does not depend on which side is the left one, `MatchedCount` is the same when the arguments are swapped.
- **Among assignments that match equally many nodes**, the one pairing the most occurrences of the same shape is taken, then the one making the most pairs; then the one that reports the fewest differences, then the fewest type mismatches, then the most case-only differences (`ValueCaseMismatch`, `PropertyNameCaseMismatch`), and under `JJScoringMode.Weighted` the one whose differences weigh least, each difference's weight times the nodes under it on both sides. These later levels read nothing but the differences a pair would report, so they do not depend on where an element stands in its array: `["x", 1]` against `["X", true]` pairs the two strings whatever the order of the elements, instead of pairing by position and reporting two type mismatches in one order and a case mismatch and a type mismatch in the other. Only what is still tied after all of that is decided by position: the first left occurrence in document order gets the earliest right partner that any of the remaining assignments gives it, then the second left occurrence, and so on.
- **Not always the fewest differences.** Matching the most nodes and listing the fewest differences are different goals, because an unmatched subtree is one difference whatever its size. The array `[{ a, b, c, d }, { d, x, p, q }]` against `[{ a, b, c, d, x }, { a, b, c, y }]` is paired crosswise, which matches 7 nodes inside the pairs and lists 7 differences; pairing the first with the first would match 5 and list 3, two of them whole elements.
- **Algorithm.** Every pair that may be made is compared once, for its matched nodes and a tally of the differences it would report (their number, how many are type mismatches, how many are case-only, and under Weighted scoring their weighted mass): no difference is built or located for a candidate. The assignment is solved with the Hungarian algorithm, O(n³) for a group of n, once for the cost that encodes matched nodes, same-shape pairs and pairs, and once more for each tally level that still varies among the cells that can belong to a best assignment so far (the tight cells of the previous solve); a level that is the same in every open pair, as every level is for a group without differences, is skipped. The positional rule is applied inside the cells left, without solving again, O(n⁴) at worst. The pairs taken are then compared once more to report their differences, so a call builds differences only for pairs it reports. Before any candidate is counted, the members of the group and everything below them are given an exact identity by the per-call `ComparisonCache`: an interned canonical key of a node's kind, names and children's identities (sorted for Strict arrays, distinct for StructureOnly), with a second key that also carries the primitive values a selected rule accepts as identical to themselves. Two subtrees with one identity are matched whole, at any depth, without being walked; two with different identities are compared as described above, so identities decide nothing a comparison would not. The cost uses 64-bit integers. Unbudgeted internal calls have no unconditional overflow guarantee; public calls bound nodes and group sizes, keeping matched counts and encoded 64-bit costs in range.
- **No approximation fallback.** Public calls have an explicit group limit and fail if it is exceeded; the legacy internal call remains unlimited. Earlier versions assigned a group as a whole only up to 32 occurrences and paired a larger group greedily, with an `AssignmentLimitExceeded` flag on the result saying that `MatchedCount` might then be below the maximum. The limit was removed: the benchmarks below show that comparing every pair is the whole of the cost and the assignment itself adds nothing measurable at any size measured, so the fallback saved nothing and only made the count wrong. The flag, the limit and the `Match` overload that took it are gone; a result never needs to be checked for them.

### Paths

- `$` is the root. `[*]` stands for any element of an array; indices are not used because element position is ignored.
- A name made of letters, digits and underscores that does not start with a digit is written `.name`. Letters and digits are judged as Unicode code points (`$.café`, `$.名前`, and a letter outside the Basic Multilingual Plane such as `$.𐐀plain`), so `_` is the only ASCII symbol a plain name may hold; an Arabic-Indic digit starts a name as a digit does. Any other name, including the empty one, is written `['name']`, with `\` and `'` escaped by a backslash: `$['a.b']['e[0]']`, `$['back\\slash']`, `$.child['it\'s valid']`. Because the backslash is escaped as well, the quote that closes a name is always the one not preceded by an odd number of backslashes: the name `it\'s` is written `['it\\\'s']`.
- Inside the brackets, every character that would not show in the text is written as a JSON escape, so that a path is one printable line and two names that would look alike stay distinct. Backspace, form feed, line feed, carriage return and tab are `\b`, `\f`, `\n`, `\r` and `\t`; every other control character (U+0000 to U+001F, U+007F to U+009F), every format character (zero-width space, byte order mark, bidirectional controls, soft hyphen, tag characters), the line and paragraph separators (U+2028, U+2029), every space separator other than U+0020 (the no-break space among them) and a lone surrogate are `\uXXXX` with upper-case hex digits; a supplementary code point is written as its two surrogates, as in JSON: `$['line\nbreak']`, `$['del\u007F']`, `$['zero\u200Bwidth']`, `$['no\u00A0break']`, `$['tag\uDB40\uDC01']`. Visible characters, emoji included, are written as they are: `$['status🚀']`. The escapes are those of a JSONPath (RFC 9535) single-quoted name, so the path can be read back unambiguously, though no reader exists here. Escaping affects the path only: the `PropertyName` of a node is never changed.
- A path is for reporting. It does not identify a node: properties sharing a name share a path, and so do the element shapes of one array. `Left` and `Right` are the reliable reference.

### Order of differences

Within an object: the left properties in document order, each followed by the differences found below it, then the right-only properties in document order. Within an array: the leftover left shapes in document order, each with the differences of its pair or as a missing shape, then the right shapes without a partner.

## Edge cases and limitations

- `MatchedCount` is zero when the roots have different types.
- `MatchedCount` is the maximum possible matched count under the pairing rules, for every input. When `IsMatch` is `true` it is the size of the structure, which is the count of a node against itself; and it reaches the size of both sides only when `IsMatch` is `true`.
- Pairing counts every occurrence of one side against every occurrence of the other, so that the assignment of the whole group is the best one. Identical array elements collapse to one shape first, and a candidate pair of identical subtrees costs one lookup, so this is only costly for arrays holding many different shapes on both sides, or objects declaring one name many times with different values below. Nested arrays of many different shapes multiply the cost, one square per level. The assignment of a group of n is O(n³), and its tie rule O(n⁴) in a worst case that no measured input came near; both are dwarfed by the n² subtree comparisons when the candidates differ.
- Pairing counts every node as one, under `JJScoringMode.Weighted` too: the facade applies the weights to the differences of this pairing afterwards. The weights only decide among pairings that match equally many nodes, so a pairing that matches fewer nodes but would leave a lighter weighted mass unmatched is not sought.
- Paths escape what is invisible, not what is confusable: the precomposed and decomposed `café`, or a Latin and a Cyrillic `а`, give paths that look alike and differ only in their code points. Escaping also breaks a sequence apart visually when it holds a format character (a zero-width joiner in an emoji sequence, the tag characters of a flag).
- The result is not symmetric in its ordering: swapping the arguments swaps the mismatch types, and the positional rule follows the left side. Which occurrences are paired can still differ between two orders of one array only when assignments tie on every level above the positional one, including the differences they report; then the reported paths change, but not the number or the kinds of the differences, nor the weighted mass under Weighted scoring.
- Public calls bound parsed nesting depth to 512 through settings validation; internal unbudgeted callers remain responsible for depth.
- A string that could not be decoded (`Value` `null`) is a `String` like any other.

## Tests

`tests/JJMatch.Tests/Matching/StructureMatcherTests.cs`. Every test compares sections of `Left.json` and `Right.json`; some compare two different sections with each other to get a scenario the fixtures do not hold side by side.

- a document against itself;
- values, `true` against `false`, property order and array element order ignored (`id`, `active`, `primitiveValues`, `propertyOrder`, `propertyOrderInArrays`, `arrayElementOrder`, `idListOrder`, `nestedArrayElementOrder`);
- repeated element shapes ignored (`options`, `primitives`);
- type mismatches, with nothing reported below them (`payment`, `notes`, `attributes` as an object and as an array);
- missing and extra properties at several depths (`meta`), and an unmatched subtree reported once (`shipping`, `caseVariants` in the whole documents);
- a case-only property rename reported as one `PropertyNameCaseMismatch` instead of a missing and an extra property, under every `PropertyNameMode` (`numbers.uint64Max`/`UInt64Max`, `publicApi.propertyNames`), with no effect on `MatchedCount` or the Standard score;
- path formatting (`a.b`, `e[0]`, and `escapedNames` against `unicodeNormalization`) and case-sensitive names (`caseVariants`);
- single quotes in property names, at the top and nested, preserved and escaped in paths, with a name holding a backslash before a quote (`quotedNames`);
- control characters escaped in paths while the emoji is written as it is (`controlCharacters` against `supplementaryUnicode`); every invisible character escaped, each by its short escape or as `\uXXXX`, a supplementary letter written plain, a name starting with an Arabic-Indic digit bracketed, and the nodes keeping their names unchanged (`pathEscapes` against the top-level `""` object); escaped names below escaped and plain parents, and an ANSI escape sequence as a name, with the section pairing up as any other (`pathEscapes`);
- lone surrogate names kept distinct from each other and from a valid pair, and written as `\u` escapes in paths (`loneSurrogates`, and against its own `nested` object);
- element shapes on one side only, nested arrays as shapes and array boundaries (`bundle.items`, `matrix`, `arrayBoundaries`);
- pairing of similar shapes, with two equally good pairings of the addresses decided by the tie rule, document order last (`customer.addresses`, `history`), and no pairing of unrelated objects (`differentIds` against `idListOrder`);
- duplicate property names, equal and with a surplus on either side (`duplicateKeys` against the top-level `""` object);
- duplicate property names of different types in a different order: a full match, a changed type, and a removed occurrence on either side (`duplicateTypeOrder`, its `retyped` object, and an `attributes` element holding one `value`);
- stringified JSON as a `String` leaf, using the roots of `LeftStringify.txt` and `RightStringify.txt`.
- duplicate property names holding objects or arrays (`duplicateStructures`): the same shapes in a different order as a full match, an occurrence of the same shape paired before the others, partially similar objects paired crosswise, and arrays of different shapes with a surplus occurrence on either side;
- assignment of a whole group where taking the best pair first matches fewer nodes, for same-named properties and for array element shapes (`assignedObjects`, `assignedElements`);
- identical shapes not paired first when another pairing matches more nodes (`assignedElements` against `exactFirstElements`);
- an identical pair preferred among equally good assignments (`bundle.items`, `matrix`, `objects` against `partialObjects`), and empty objects paired as one shape (`assignmentLimit` arrays of `Right.json`);
- equally good assignments decided by the tie rule, document order last (`objects` against `partialObjects`);
- the largest group the fixtures hold, assigned as a whole (`assignmentLimit`, 33 distinct element shapes on each side, written for the former limit of 32): against its counterpart, and against two objects that the former greedy fallback paired for 4 nodes where the best assignment matches 5, both ways round;
- groups nested inside the pairs of a group assigned on their own (`speculativeLimit`);
- the invariants of the maximum over every section of both fixtures and both whole documents, each against every other and against itself: `MatchedCount` and `IsMatch` are the same with the sides swapped, `MatchedCount` never exceeds the size of either side, and it reaches both sizes exactly when `IsMatch` is `true`.

## Benchmarks

`benchmarks/JJMatch.Benchmarks` measures `Match` alone, on trees built beforehand, and the public `JJMatcher.Match` end to end:

```text
dotnet run -c Release --project benchmarks/JJMatch.Benchmarks -- --filter "*StructureMatcher*" "*JJMatcher*"
```

Measured with a short job (.NET 10, macOS, Apple M5) before and after the 2026-10-12 optimization, with the earlier sources compiled into a copy of the benchmark project; the figures are indicative only. The full table, with the end-to-end cases, is in `Docs/Decisions.md`.

| Case | Before | After | Allocated before | Allocated after |
| --- | ---: | ---: | ---: | ---: |
| `Left.json` against `Right.json` | 0.87 ms | 0.54 ms | 4.3 MB | 1.8 MB |
| `Left.json` against itself | 0.71 ms | 0.43 ms | 3.6 MB | 1.5 MB |
| Array of 32 distinct shapes against 32 others | 0.74 ms | 0.13 ms | 3.7 MB | 0.23 MB |
| 128 against 128 | 24.8 ms | 2.3 ms | 58 MB | 2.6 MB |
| 512 against 512 | 0.43 s | 45 ms | 0.93 GB | 37 MB |
| 51,200 elements of one shape against themselves | 26 ms | 12.7 ms | 151 MB | 22 MB |

- Time and memory still grow with the square of the number of different shapes, because every candidate pair is counted; the former per-candidate difference lists, formatted paths and shape texts are gone, which is where the allocation went. Assigning the whole group costs nothing measurable on top of the counts at any size measured.
- Repeated shapes collapse to one representative first, and a candidate pair of identical subtrees costs one lookup, so the `RepeatedShape` rows now measure tree traversal and shape identification rather than comparison.

### Independent review checks

`Assignment_agrees_with_exhaustive_search_including_all_tie_breaks` enumerates every partial injection independently of the Hungarian algorithm: 21,297 matrices of dimensions 1 through 3 (forbidden, partial and identical pairs), plus 2,000 seeded weighted rectangular matrices up to 5 by 5, including zero-benefit pairs. It checks matched counts, identical-pair and pair-count preferences, and the entire document-order partner vector. These are numerical solver inputs, not generated JSON.

`Structural_equivalence_agrees_with_an_independent_recursive_relation` compares every pair of `core` sections against a recursive object-multiset and array-set equality relation that uses neither production signatures nor assignment costs. Agreement is bounded evidence, not a proof for every input or for future weighted scoring.


Public configuration also accepts `Mode`, independent `StringComparison` / `NumberComparison`, and nested `Limits`. These map to the existing matching rules; no assignment or score algorithm changes. Explicit legacy options override Mode. `IncludeStructuralScore = false` skips the extra diagnostic comparison. See the current Guide and configuration decision of 2026-10-31.

`Every_assignment_priority_agrees_with_independent_exhaustive_search` checks 5,000 seeded rectangular matrices up to 5×5 against enumeration of all partial injections. Its objective tuple includes matched nodes, identical pairs, pair count, difference count, type mismatches, case-only differences and signed weighted mass, followed by document-order ties. Weighted and Standard cases alternate. Four directed cases make each later priority decide a tie while all subsequent levels favor the losing choice. Existing public pipeline tests separately verify construction of weighted costs including unpaired subtrees.
