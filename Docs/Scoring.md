# Score semantics

## Selected measure and research

JJMatch uses the **Sørensen–Dice coefficient**, expressed as a percentage, over a maximum one-to-one correspondence of JSON nodes permitted by the selected matching rules:

```text
Score = 100 × 2M / (L + R)
```

`L` and `R` count the effective nodes in each document. `M` counts matched node pairs. The standard Dice dissimilarity is unmatched membership divided by total membership; subtracting it from one gives `2M / (L + R)`. This choice normalizes overlap symmetrically without inventing a weighted blend of separate structure and value scores. [SciPy's Dice definition](https://docs.scipy.org/doc/scipy/reference/generated/scipy.spatial.distance.dice.html)

The node representation and allowed correspondence below are JJMatch's domain choices, not properties dictated by Dice. Tree similarity research establishes maximum common subtree matching as a well-defined optimization problem and uses assignment subproblems. JJMatch reuses its existing recursive assignment engine with stricter JSON-specific rules; it does **not** implement the unrestricted subtree algorithm or claim its complexity bounds. [Droschinsky, Kriege and Mutzel, *Faster Algorithms for the Maximum Common Subtree Isomorphism Problem*](https://arxiv.org/abs/1602.07210)

Alternatives considered: Jaccard also normalizes overlap but uses union in the denominator; raw edit counts need edit operations and costs before normalization; averaging independent structure/value percentages obscures missing-value denominators and requires arbitrary component weights. Dice directly answers how much of both documents participates in accepted matches. No weights or configurable success threshold are used.

## What counts

Every JSON value is one node, including the root and every object/array container. A property contributes its value node; its name labels that node and does not earn an additional point. Two same-type containers earn one pair even when their children differ. Empty matching containers therefore score 100 without a special case. Present JSON null is a node; absence is not.

Primitive nodes earn one pair when a selected value rule accepts them, otherwise zero. StructuralMatching alone instead accepts primitives of the same structural type, with true and false belonging to one Boolean type. There is no fractional credit inside a fuzzy value: it is either accepted or rejected. Property names are compared exactly unless `PropertyNameMode` is `Normalized`, which pairs names that differ only in case, outer white space or Unicode form; either way a name earns no point of its own.

Roots must correspond to roots. Descendants can correspond only within paired containers, and traversal stops below a type mismatch. Object properties pair only with the same name under the property name rule, regardless of order. Repeated names are retained and assigned one-to-one. Strict arrays pair by position; Unordered arrays assign occurrences one-to-one across the entire array, with duplicates counted separately. Unordered candidates can have different types, yielding zero shared nodes for that pair and a type difference.

StructureOnly arrays first collapse structurally identical element shapes recursively. One representative of each shape contributes to `L`, `R` and `M`. Values throughout those array subtrees are ignored, even if value matching is otherwise enabled. Two shapes are eligible to pair when they are primitives of one structural type, two arrays, or objects sharing a property name (or both empty). Values outside arrays still use the selected value rules. The denominator uses exactly the same shape reduction as comparison.

## Optimality, endpoints and symmetry

Every permitted candidate subtree pair is compared. Assignment maximizes the sum of matched nodes. Recursive child subproblems are independent because each occurrence is used at most once. Thus this optimizes the stated correspondence, not a flattened bag of paths or unrestricted tree edit distance. Among equal maxima, fully matching pairs are preferred, then more pairs, then the earliest available right partner for each left occurrence in document order. Tie decisions never change the score.

Each matched node consumes one node from each side, so `0 <= M <= min(L,R)` and `0 <= Score <= 100`. Each document has a root, so the denominator is positive. Equality at 100 requires every effective node on both sides to be accounted for and accepted; that is exactly the condition for an empty difference list and `IsMatch == true`. Every mismatch loses at least one node membership. With the public node-count cap, double precision cannot round such a loss to 100. No rounding, clipping or epsilon threshold is used internally.

The denominator and maximum correspondence size are unchanged on swapping inputs, giving symmetric Score and IsMatch. Difference direction, order and tied pair choices are left-oriented and need not simply reverse. Repeated calls with identical ordered inputs, settings and runtime produce identical results. Unicode folding depends on the runtime's Unicode tables, so cross-version character behavior is not promised.

## Worked examples

| Inputs and policy | L | R | M | Score |
| --- | ---: | ---: | ---: | ---: |
| `{"name":"Ann","age":30}` vs age 31, default | 3 | 3 | 2 | 66.666… |
| `[1,2,3]` vs `[3,2,1]`, Strict | 4 | 4 | 2 | 50 |
| Same arrays, Unordered | 4 | 4 | 4 | 100 |
| `[1,2]` vs `[1,2,3]`, Strict or Unordered | 3 | 4 | 3 | 85.714… |
| `{"a":1}` vs `{"b":2}`, default | 2 | 2 | 1 | 50 (shared object container) |
| `1` vs `2`, default | 1 | 1 | 0 | 0 |
| `[]` vs `{}`, any policy | 1 | 1 | 0 | 0 |
| `[{"id":1},{"id":2}]` vs `[{"id":9}]`, StructureOnly arrays | 3 | 3 | 3 | 100 |

## Limits of interpretation

Equal unit weights make deeply nested or large subtrees count more than shallow ones. Shared containers are meaningful structural overlap even when no leaf values match; 0 means no node matches under the permitted correspondence, not a domain judgment. An important identifier has no special weight. A large missing subtree lowers the score according to all its nodes but produces only one diagnostic, so counting differences cannot reproduce the score.

Fuzzy acceptance is nontransitive. The score is not a probability, semantic truth measure, or mathematical distance metric; neither the triangle inequality nor equivalence classes are promised. Adding a shared node generally raises a partial score; changing array mode or value rules changes the comparison problem, so scores across policies need not be comparable. More permissive primitive acceptance cannot lower the optimal score under a fixed array policy and denominator.

Normalized and Fuzzy alone accept only strings; Numeric alone accepts only numbers. Consequently identical mixed-type documents need not match under those single flags. Include Exact to preserve identity for the other primitive kinds. ValueMode.None is rejected when values are compared.

Resource limits fail the operation instead of returning an approximation. Cancellation is cooperative and the work budget is an implementation allowance, not a time or memory guarantee. Parsing and individual runtime operations such as Unicode normalization or cloning a snapshot are bounded by input limits but cannot be interrupted midway. Unordered arrays and repeated names are more expensive than positional arrays; groups are capped before allocating assignment matrices. The public depth and node caps bound recursion and integer score arithmetic. Internal legacy calls without a budget retain their previous unrestricted behavior.

`StructuralMatchPercentage` recomputes structure-only similarity under the same array policy. `ValueMatchPercentage` is the main value-aware document Score, including required structural context; it is not a separate component to average with the structural score. These diagnostic properties do not change the three-output contract.
