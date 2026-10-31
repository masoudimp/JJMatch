# Score semantics

For configuration and presets, see the [quick guide](../Guide.md). This reference uses the internal/legacy policy names: Strict is `ArrayComparison.Unordered`, StructureOnly arrays are `ArrayComparison.DistinctShapes`, and StructuralMatching is `Mode = StructureOnly`.

## Selected measure and research

JJMatch uses the **Sørensen–Dice coefficient**, expressed as a percentage, over a maximum one-to-one correspondence of JSON nodes permitted by the selected matching rules:

```text
Score = 100 × 2M / (L + R)
```

`L` and `R` count the effective nodes in each document. `M` counts matched node pairs. The standard Dice dissimilarity is unmatched membership divided by total membership; subtracting it from one gives `2M / (L + R)`. This choice normalizes overlap symmetrically without inventing a weighted blend of separate structure and value scores. [SciPy's Dice definition](https://docs.scipy.org/doc/scipy/reference/generated/scipy.spatial.distance.dice.html)

The node representation and allowed correspondence below are JJMatch's domain choices, not properties dictated by Dice. Tree similarity research establishes maximum common subtree matching as a well-defined optimization problem and uses assignment subproblems. JJMatch reuses its existing recursive assignment engine with stricter JSON-specific rules; it does **not** implement the unrestricted subtree algorithm or claim its complexity bounds. [Droschinsky, Kriege and Mutzel, *Faster Algorithms for the Maximum Common Subtree Isomorphism Problem*](https://arxiv.org/abs/1602.07210)

Alternatives considered: Jaccard also normalizes overlap but uses union in the denominator; raw edit counts need edit operations and costs before normalization; averaging independent structure/value percentages obscures missing-value denominators and requires arbitrary component weights. Dice directly answers how much of both documents participates in accepted matches. No configurable success threshold is used. This unweighted score is `JJScoringMode.Standard`, the default; the opt-in weighted mode is defined in its own section below and changes only the measure, never the alignment.

## What counts

Every JSON value is one node, including the root and every object/array container. A property contributes its value node; its name labels that node and does not earn an additional point. Two same-type containers earn one pair even when their children differ. Empty matching containers therefore score 100 without a special case. Present JSON null is a node; absence is not.

Primitive nodes earn one pair when a selected value rule accepts them, otherwise zero. StructuralMatching alone instead accepts primitives of the same structural type, with true and false belonging to one Boolean type. There is no fractional credit inside a fuzzy value: it is either accepted or rejected. A rejected pair of strings whose normalized texts are equal ignoring case still earns no point, but is reported and weighted as one lighter `ValueCaseMismatch` instead of a full value mismatch; see Weighted scoring below. Property names are compared exactly unless `PropertyNameMode` is `Normalized`, which pairs names that differ only in case, outer white space or Unicode form; either way a name earns no point of its own. Under `None`, a property whose name only differs this way, with neither side holding the other's exact name, still earns no point, but is reported and weighted as one lighter `PropertyNameCaseMismatch` instead of a full missing and a full added property; see Weighted scoring below.

Roots must correspond to roots. Descendants can correspond only within paired containers, and traversal stops below a type mismatch. Object properties pair only with the same name under the property name rule, regardless of order. Repeated names are retained and assigned one-to-one. Strict arrays assign occurrences one-to-one across the entire array, wherever the elements stand, with duplicates counted separately. Candidates can have different types, yielding zero shared nodes for that pair and a type difference.

StructureOnly arrays first collapse structurally identical element shapes recursively. One representative of each shape contributes to `L`, `R` and `M`. Values throughout those array subtrees are ignored, even if value matching is otherwise enabled. Two shapes are eligible to pair when they are primitives of one structural type, two arrays, or objects sharing a property name (or both empty). Values outside arrays still use the selected value rules. The denominator uses exactly the same shape reduction as comparison.

## Optimality, endpoints and symmetry

Every permitted candidate subtree pair is compared. Assignment maximizes the sum of matched nodes. Recursive child subproblems are independent because each occurrence is used at most once. Thus this optimizes the stated correspondence, not a flattened bag of paths or unrestricted tree edit distance. Among equal maxima, fully matching pairs are preferred, then more pairs, then the earliest available right partner for each left occurrence in document order. Tie decisions never change the score.

Each matched node consumes one node from each side, so `0 <= M <= min(L,R)` and `0 <= Score <= 100`. Each document has a root, so the denominator is positive. Equality at 100 requires every effective node on both sides to be accounted for and accepted; that is exactly the condition for an empty difference list and `IsMatch == true`. Every mismatch loses at least one node membership. With the public node-count cap, double precision cannot round such a loss to 100. No rounding, clipping or epsilon threshold is used internally.

The denominator and maximum correspondence size are unchanged on swapping inputs, giving symmetric Score and IsMatch. Difference direction, order and tied pair choices are left-oriented and need not simply reverse. Repeated calls with identical ordered inputs, settings and runtime produce identical results. Unicode folding depends on the runtime's Unicode tables, so cross-version character behavior is not promised.

## Worked examples

| Inputs and policy | L | R | M | Score |
| --- | ---: | ---: | ---: | ---: |
| `{"name":"Ann","age":30}` vs age 31, default | 3 | 3 | 2 | 66.666… |
| `[1,2,3]` vs `[3,2,1]`, Strict | 4 | 4 | 4 | 100 |
| `[1,2]` vs `[1,2,3]`, Strict | 3 | 4 | 3 | 85.714… |
| `{"a":1}` vs `{"b":2}`, default | 2 | 2 | 1 | 50 (shared object container) |
| `1` vs `2`, default | 1 | 1 | 0 | 0 |
| `[]` vs `{}`, any policy | 1 | 1 | 0 | 0 |
| `[{"id":1},{"id":2}]` vs `[{"id":9}]`, StructureOnly arrays | 3 | 3 | 3 | 100 |

## Weighted scoring (opt-in)

`JJMatchSettings.Scoring` holds a `JJScoringSettings` and is never null: a new settings object carries a configuration whose `Mode` is `Standard`, which is the score above unchanged, and assigning null throws. `JJScoringMode.Weighted` keeps the same maximum correspondence, the same node counting and the same differences, and replaces the measure by a **weighted Sørensen–Dice** over node weights:

```text
Score = 100 × 2 × MaxWeight × M / (2 × MaxWeight × M + Σ over differences d of w(d) × n(d))
```

- A matched node weighs `MaxWeight` (10) on each side, so the matched mass is `2 × 10 × M`.
- Every difference `d` has a public `JJDifferenceKind` and `n(d)` unmatched nodes, counted exactly as `L` and `R` count nodes: both primitives of a `ValueMismatch` or `ValueCaseMismatch` (2), the nodes of both subtrees of a `TypeMismatch`, the nodes of both properties of a `PropertyNameCaseMismatch`, the nodes of the left subtree of a `Missing`, the nodes of the right subtree of an `Added`. Under StructureOnly arrays these are the shape-reduced sizes of the denominator.
- `w(d)` is the configured weight of that kind, an integer from `MinWeight` (1, least important) to `MaxWeight` (10, most critical): `TypeMismatchWeight` 10 (the shape a consumer relies on is broken, and nothing below it is compared), `MissingWeight` 8 (expected content is absent), `ValueMismatchWeight` 6 (the shape holds, the content does not), `PropertyNameCaseMismatchWeight` 4 and `ValueCaseMismatchWeight` 4 (the content is still there, only its spelling's case, outer white space or Unicode form differs) and `AddedWeight` 3 (everything expected is still there) by default. A weight outside 1 to 10, or an undefined mode, throws `ArgumentOutOfRangeException`; the weights are validated under `Standard` too, where they play no part.

**No double counting.** Nothing below a reported difference is compared or reported again, and every unmatched node lies below exactly one difference, so `Σ n(d) = L + R − 2M`. A missing subtree therefore weighs its whole size once, under its one difference, and a type mismatch weighs the nodes of both sides once, because each side's nodes are distinct members of the denominator. With every weight at 10 the quotient is `2 × 10 × M / (10 × (L + R))`, the same rational number as `2M / (L + R)`, and both modes compute it as one correctly rounded division of exactly representable integers, so they then agree bit for bit.

**Properties.** `0 ≤ Score ≤ 100`. It is 100 exactly when there is no difference, which is `IsMatch`, and 0 exactly when `M = 0`, whatever the weights. The denominator is positive because both documents have a root. The score is deterministic. Weights discount, never amplify: a `Weighted` score is never below the `Standard` one, lowering a weight never lowers the score, raising one never raises it, and a kind that is not reported has no effect. Swapping the documents turns every `Missing` into an `Added` and back, so the weighted score is symmetric exactly when `MissingWeight` equals `AddedWeight`, or when the two weights are swapped together with the documents; this is deliberate, since the left document is the expected one. `IsMatch` and `Differences` never depend on the mode or the weights; each `JJDifference` reports the weight applied to it as `ScoreWeight`, the configured weight of its kind under `Weighted` and 10 under `Standard`. `StructuralMatchPercentage` applies the same weights to the structure-only pass, where no `ValueMismatch` or `ValueCaseMismatch` occurs; `ValueMatchPercentage` is the weighted `Score` as before.

**What the weights do not do.** The correspondence is still the one that matches the most nodes with unit weights (see [Match.md](Match.md)); the weights are applied to the differences of that correspondence afterwards, so a pairing that would leave a lighter weighted mass unmatched is not sought. Weights are per public kind: the internal duplicate-count category takes the `Missing` or `Added` weight of the side it is reported on, and containers and leaves weigh alike within a kind. Scores under different weight configurations measure different things and are comparable only through the ordering guarantees above.

| Inputs (left is the expected document), default policy | Standard | Weighted, default weights | Weighted unmatched mass against matched mass |
| --- | ---: | ---: | --- |
| `{"name":"Ann","age":30}` vs age 31 | 66.666… | `4000 / 52` = 76.923… | `2 × 6 = 12` against `2 × 10 × 2 = 40` |
| Same pair with `ValueMismatchWeight = 10` | 66.666… | 66.666… | `20` against `40`, the Standard quotient |
| `[1,2]` vs `[1,2,3]` | 85.714… | `6000 / 63` = 95.238… | one added node, `3` against `60` |
| `[1,2,3]` vs `[1,2]` | 85.714… | `6000 / 68` = 88.235… | one missing node, `8` against `60` |
| `[]` vs `{}` | 0 | 0 | `2 × 10 = 20` against `0` |
| `publicApi.nested` fixtures (city, `old`, `nullable` and the added array `[1]`) | 62.5 | `10000 / 134` = 74.626… | `2 × 6 + 2 × 8 + 2 × 3 = 34` against `100` |
| Same fixtures with `AddedWeight = 10` | 62.5 | `10000 / 148` = 67.567… | the added array weighs its two nodes: `12 + 16 + 20` |
| [Worked order pair](Configuration.md#2-the-example-documents), Strict, default values | 70 | `28000 / 347` = 80.691… | `4 × 2 × 6 + 1 × 2 × 4 + 8 + 3 = 67` against `280`; the normalized-equal name is a ValueCaseMismatch |
| `publicApi.propertyNames`, `PropertyNameMode = None` (id/ID, name/" Name ", café's two Unicode forms and the two skus fold together; postalCode/postal_code do not) | 40 | `8000 / 131` = 61.068… | `5 × 2 × 4 + 8 + 3 = 51` against `80` |

## Limits of interpretation

Equal unit weights make deeply nested or large subtrees count more than shallow ones, and so does the weighted mode within one kind of difference. Shared containers are meaningful structural overlap even when no leaf values match; 0 means no node matches under the permitted correspondence, not a domain judgment. An important identifier has no special weight: the weighted mode weighs kinds of difference, not paths. A large missing subtree lowers the score according to all its nodes but produces only one diagnostic, so counting differences cannot reproduce the score.

Fuzzy acceptance is nontransitive. The score is not a probability, semantic truth measure, or mathematical distance metric; neither the triangle inequality nor equivalence classes are promised. Adding a shared node generally raises a partial score; changing array mode or value rules changes the comparison problem, so scores across policies need not be comparable. More permissive primitive acceptance cannot lower the optimal score under a fixed array policy and denominator.

Normalized and Fuzzy relax only strings and Numeric only numbers. Identical primitives of every kind match under any selection, `None` included, so a document always matches itself. `None`, the default, accepts identical values only.

Resource limits fail the operation instead of returning an approximation. Cancellation is cooperative and the work budget is an implementation allowance, not a time or memory guarantee. Parsing and individual runtime operations such as Unicode normalization or cloning a snapshot are bounded by input limits but cannot be interrupted midway. Arrays and repeated names are assigned as groups, which costs one comparison per candidate pair; groups are capped at `MaxAssignmentSize` before allocating assignment matrices, so a Strict array longer than the cap fails the operation. The public depth and node caps bound recursion and integer score arithmetic. Internal legacy calls without a budget retain their previous unrestricted behavior.

`Diagnostics.StructuralScore` (also exposed by legacy `StructuralMatchPercentage`) reports structure-only similarity under the same array policy and scoring mode. Set `IncludeStructuralScore = false` to skip this diagnostic and return null; it remains enabled by default for compatibility. `ValueMatchPercentage` is the main value-aware document Score, including required structural context; it is not a separate component to average with the structural score. These diagnostic properties do not change the three-output contract.
