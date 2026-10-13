# JJMatch technical review

> Historical review of the internal-only implementation. The subsequently authorized public API work implements matching, all three array policies, value-aware alignment, researched Dice scoring, occurrence-aware differences, input handling and resource guards. See [current decisions](Decisions.md#public-api-completion--2026-10-12-current-decisions), [scoring](Scoring.md) and the [consumer guide](../Guide.md). The findings, timings and original validation below describe the earlier revision; they are not current public API benchmarks.

Reviewed on 2026-10-12, starting from commit `8d6e7d2965d4d4b33135e8c37ab838e08c6f5959`.

JJMatch has a substantial internal structural matcher and four primitive value rules, but it is not yet a usable public JSON comparison library. The highest priority is to agree on an honest, small public contract, including input handling and resource limits. No new false match or false non-match was reproduced in the implemented rules during this review. That is bounded evidence from the checks below, not a universal correctness claim.

This review covers every production component, active and excluded tests, fixtures, benchmarks, package metadata, documentation, and CI/publishing workflows. Production algorithms, target frameworks and matching semantics remain unchanged. Safe changes strengthen independent validation, restore the fixture convention and correct misleading documentation. Optimization remains deferred under the existing decision.

## Prioritized findings

| Priority | Classification | Evidence and consequence | Resolution |
| --- | --- | --- | --- |
| P0 before release | Verified public feature gap | `JJMatcher.Match` has an empty body and returns `void`. It cannot report a result, validate input or apply any option. README examples use nonexistent `Compare`, `JJMatchWeights`, `JJDifference` and `Differences`. | README, XML and package metadata now disclose this. Public implementation needs approval of a contract; no invented percentages were implemented. |
| P1 | Verified fixture convention violation | Three files in `TestData/StressTestingJsons` supplied active tests despite the four-file rule. | Consolidated into `core`, `stress` and `stressChanges` sections of the approved pair; removed redundant files and synchronized both TXT files. All prior assertions remain. |
| P1 | Verified complexity documentation defect | `Quantity` calls `BigInteger.Parse` on the exponent while its comments claimed linear cost for every input and that no input is expensive. The .NET 8 parser includes repeated multiply/add conversion, and measured scaling below is not linear. | Corrected comments and decisions. Exact arithmetic remains intact. |
| P1 | Known resource limitation | `Traverse`, ID extraction and structural comparison recurse; `TryPlace` also recurses with assignment-group width, independently of JSON depth. No cancellation, node/group/string budget or arithmetic guard exists. | No crash was deliberately triggered. A bounded public input contract and process-isolated stress checks are proposed before exposure to untrusted workloads. |
| P1 | Verified coverage gap, now reduced | Structural invariant tests could accept a symmetric but suboptimal solver; they did not independently enumerate assignments. Fuzzy's reference used production normalization. | Added exhaustive and seeded assignment oracles, a separate structural equivalence relation, and independent runtime-based normalization for the fuzzy oracle. |
| P2 | Measured performance limitation | Equal folded strings still fill the quadratic fuzzy table. A 2,048-unit pair took about 6.2 ms and 28.8 KB per call in this environment. | Benchmark added; no optimization implemented. |
| P2 | Unsupported safety claim | `long` assignment costs were described as unable to overflow for any in-memory input. Scores contain `matchedCount × (groupSize + 1)²`; counts themselves use `int`. Memory capacity is not a proof of those bounds. | Corrected the guarantee. No realizable overflow was reproduced at ordinary sizes; this remains an extreme-input risk, not a demonstrated false result. |
| P2 | Verified documentation drift | Stress tests already raised `MaxDepth` to 128, although decisions said no caller did. Some docs still described identical shapes as paired first and Fuzzy as unimplemented. | Corrected status and historical claims. |
| P2 | Deferred compatibility validation | Local host has .NET 10.0.11 only. Native .NET 8 execution could not start; Framework/Mono/Unity/MAUI, trimming, AOT and invariant globalization were not established by this run. | Both shipped assemblies were exercised using explicit .NET 10 roll-forward. Existing CI installs native .NET 8 and .NET 10 on three operating systems. |

## Concrete examples and contract boundaries

**Public API:** passing the fixture strings to `JJMatcher.Match` executes no comparison. The documented `JJMatcher.Compare(left, right)` cannot compile because that method does not exist. This is a release-blocking capability gap, not evidence that an internal matcher computes an incorrect result.

**Fuzzy is not transitive:** in `core.fuzzyValues`, `Left.swappedPairs` is `Jhon Smiht`, `Left.typo` is `Jon Smith`, and `Right.manyEdits` is `Jon Smyth`. The first two match and the last two match; the endpoints do not. A regression pins this. It would be incorrect to use fuzzy similarity as hash-set equality or to merge value-based array representatives transitively. The current structural matcher does neither.

**The threshold can accept changed identifiers:** `SKU-10000` against `SKU-10001` is one substitution within the allowance for nine units. That is documented Fuzzy behavior, not a bug under the current rule. Token-aware digits, per-path policies, shorter-name allowances, accent folding, Unicode scalar or grapheme distance, and unrestricted Damerau distance all require semantic approval.

**OSA is a specific distance:** optimal string alignment restricts repeated edits of substrings. The standard distance counterexample is `CA` → `AC` (1), `AC` → `ABC` (1), while OSA(`CA`, `ABC`) is 3. Unrestricted Damerau–Levenshtein gives 2 for the last pair. This theoretical example is not a newly generated JSON test. A switch of distance would alter outcomes; metric-tree pruning that assumes the triangle inequality is unsuitable for OSA. See the [stringdist research paper](https://journal.r-project.org/articles/RJ-2014-011/).

**Numeric precision is intentionally stronger than floating point:** the existing `beyondDouble` pair, `9007199254740992` and `9007199254740993`, must differ. Extreme exponent forms such as `1e99999999999999999999` and `0.1e100000000000000000000` must match. Mantissa digits and an exact exponent are the right representation; replacing them with `double` or `decimal` would break these contracts.

**Names and occurrences are preserved:** ordinal property names distinguish precomposed and decomposed text. Duplicate keys are retained and paired as a multiset. Empty containers, missing properties, null and empty strings remain distinct at the tree level. A decoded invalid-byte string has no value and matches nothing under value rules; it still has a String structural type. Lone surrogate escapes are deliberately preserved as UTF-16 units.

**Input normalization remains undefined publicly:** a root JSON string is currently a primitive to the internal matchers. The two TXT files are verified to decode to the exact source JSON, but this is fixture integrity, not an implemented public normalization pipeline. Approval must settle one-layer versus repeated unwrapping, allowed root kinds, null/malformed input, parser options, size limits and error behavior. Nested strings that resemble JSON should retain the existing literal-value semantics.

## Algorithm and architecture assessment

The traversal tree is justified. It retains types, array boundaries, ordering, duplicate occurrences and primitive spellings independently of `JsonDocument` lifetime. The extracted ID dictionary loses several of those distinctions and cannot serve as a canonical document identity or matcher input. Keep structural extraction, structural matching, value rules and future scoring separate.

The current canonical shape text has a decodable grammar: primitive kinds are single digits, containers have distinct delimiters, and property names have a length prefix. Names containing delimiters cannot consume the following shape because their lengths determine their boundaries. Object entries retain multiplicity; array entries remove duplicate shapes. No signature collision was reproduced. A hash optimization would require structural equality verification after hash collisions; a hash alone must not decide equivalence.

Whole-group assignment is appropriate for the documented objective. For a fixed group size `n`, the weight `(n + 1)²` for each matched node dominates every possible sum of identical-pair and pair-count preferences; `(n + 1)` gives identical pairs priority over pair count. Hungarian assignment maximizes that encoded objective, and tight-edge rearrangement supplies the document-order tie rule. Recursive pair scores are additive over disjoint subtrees. These arguments depend on the existing eligibility rules and arithmetic staying in range.

`MatchedCount` is consequently an optimization objective under those structural pairing rules. It does not establish optimal value alignment, minimum number of reported differences, weighted similarity, or a future global score. Greedy pairing or taking identical pairs first can lose the current optimum; the existing counterexamples must stay. New brute-force tests check the complete partner vector rather than only the total.

The largest structural allocation opportunity is that `CompareAll` constructs paths and difference lists for speculative pairs that will mostly be discarded. A later refactor could compute pair costs first, retain decisions and materialize differences for selected pairs. That would introduce either repeated comparison or a decision cache, so it needs measurement and careful validation of nested tie behavior. It is not automatically simpler than the current implementation.

Other alternatives, in increasing scope:

- After approval to optimize, an ordinal equality check on the already folded strings can bypass fuzzy dynamic programming without changing the rule. Measure unequal inputs too; do not assume every workload improves.
- Banded OSA can retain three rows while evaluating only cells within the edit allowance. Since the allowance grows with length, it remains quadratic in the worst case. Removing common prefixes or suffixes needs a proof around transposition boundaries.
- Per-call cached shapes can reduce repeated traversal and string creation. Structural interning adds complexity and must preserve exact duplicate-name semantics and representative ordering.
- A signed decimal exponent representation with bounded carry/borrow could avoid arbitrary-precision decimal conversion, but is harder to audit than `BigInteger`; keep the existing implementation until exponent workloads justify replacing it.
- An iterative traversal avoids nesting-based stack overflow but does not bound total nodes, assignment width or allocation. Input budgets and cancellation remain separate requirements.

The raw-name fallback is complex for a reason: it must preserve lone surrogates while rejecting invalid UTF-8 names without failing on invalid UTF-8 values elsewhere. Replacing it with `GetRawText` would revive an existing regression. Target compatibility also prevents simply using newer runtime-only raw-byte APIs. This component should not be simplified without its byte-damage tests and both shipped assemblies.

## Evidence and validation

Baseline: 183 passed, 1 intentionally skipped on native .NET 10. The native .NET 8 test host failed to start because its runtime is absent. Final validation passed 189 tests with one intentional skip per test target (378 passes total). Both targets ran on runtime 10.0.11; the net8.0 test target exercised the netstandard2.0 library via explicit roll-forward, not a native .NET 8 installation. Release packaging produced both the NuGet and symbol packages, and all 29 benchmark cases completed a Dry smoke run. `git diff --check` passed. No commit or publication was performed.

After the changes, the suite contains six additional cases: two independent structural tests, two value-rule tests, and two TXT synchronization cases. Existing test scenarios and assertions are retained.

Reproduce the local validation with `DOTNET_ROLL_FORWARD=Major dotnet test JJMatch.slnx --no-restore -c Release`; on a machine with both runtimes installed, omit the environment variable. `dotnet pack src/JJMatch/JJMatch.csproj -c Release --no-restore` verifies packaging.

The assignment oracle checks all 21,297 matrices with 1–3 rows and columns over three states (forbidden, one matched node with differences, one matched node without differences), and 2,000 deterministic weighted matrices up to 5×5 with zero-benefit pairs. The reference enumerates partial injections and compares objective tuples directly, without using the production scalar cost, potentials or augmenting paths. Numerical matrices are not JSON fixtures.

The structural oracle checks every pair of core sections using recursive multiset equality for object properties and set equality for array shapes. It does not call production `Shape`, `CanPair` or assignment to derive expectations. Existing numeric tests use independent integer arithmetic, and existing fuzzy tests keep the full OSA table without early termination. The latter now use independent normalization too.

Fixture consolidation was verified against the starting Git revision by comparing the ordered raw JSON token streams of both original core documents and both stress documents. This preserves duplicate keys, escaped string spellings and number spellings; only wrapper indentation changes. Catalogue excerpts were adjusted for that indentation after the original substring assertion caught the difference. No assertion was weakened. The old excluded `JJMatcherTests.cs` remains excluded and unchanged: it describes undefined APIs/scoring and still needs an approved migration rather than being silently enabled or deleted.

## Measured performance

BenchmarkDotNet 0.15.4, Release, .NET SDK 10.0.400, runtime 10.0.11, Arm64 RyuJIT, concurrent workstation GC, Apple M5, macOS 26.6.2. Each ShortRun used one launch, three warmups and three measured iterations. Process priority elevation was denied; results describe this machine and run. Some build/test activity overlapped portions of the baseline, and three iterations give wide confidence intervals. Treat these as exploratory measurements, especially nanosecond differences, not publication-grade rankings.

Parsing and tree construction run in setup, outside timing. Fuzzy inputs are distinct string instances containing repeated letters; exponent inputs are valid synthetic scalar numbers. Generated benchmark workloads are allowed by the fixture convention. Benchmarks selected the same core values before and after fixture consolidation. No runtime algorithm was changed, so these are baselines, not before/after optimization claims.

| Fuzzy operation | UTF-16 length | Mean | Allocated per call |
| --- | ---: | ---: | ---: |
| Equal after folding | 32 | 1.623 µs | 568 B |
| One substitution | 32 | 1.781 µs | 656 B |
| Equal after folding | 256 | 102.757 µs | 3,704 B |
| One substitution | 256 | 99.970 µs | 4,240 B |
| Equal after folding | 2,048 | 6,220.721 µs | 28,792 B |
| One substitution | 2,048 | 6,340.069 µs | 32,912 B |

An eightfold length increase gave roughly a sixtyfold time increase in the larger equal case, consistent with the full product-of-lengths table. Identical folded input receives no shortcut. This is actionable evidence for a later small optimization; neither banding nor pooling has been implemented.

| Numeric exponent digits | Mean | Allocated per call |
| ---: | ---: | ---: |
| 16 | 186.7 ns | 368 B |
| 256 | 1,902.0 ns | 1,744 B |
| 4,096 | 79,715.8 ns | 23,472 B |

Increasing exponent length from 256 to 4,096 digits (16×) increased observed time about 42×. This supports removing the unconditional linear claim, not assigning a universal exponent to runtime complexity. The [.NET 8 BigInteger parser source](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Runtime.Numerics/src/System/Numerics/BigNumber.cs) independently demonstrates that exponent conversion is more than a linear text scan.

### Equivalent Microsoft comparison

`NumericComparisonBenchmarks` compares scalar quantity equality on pre-parsed numbers. `JsonElement.DeepEquals` defines decimal numeric equality without rounding, which agrees with JJMatch Numeric on these inputs. Whole-document semantics differ: Microsoft preserves array order and repeated-property order, whereas JJMatch's current structural matcher ignores array order, multiplicity of array shapes and primitive values. See [Microsoft's equality contract](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.deepequals?view=net-10.0).

| Scenario | Microsoft mean | JJMatch mean | Microsoft allocation | JJMatch allocation |
| --- | ---: | ---: | ---: | ---: |
| Adjacent integers beyond double precision, unequal | 17.26 ns | 13.59 ns | 0 B | 0 B |
| `123` and `123.0`, equal | 17.70 ns | 28.65 ns | 0 B | 120 B |
| Equivalent values beyond decimal range | 21.62 ns | 66.14 ns | 0 B | 216 B |

JJMatch allocates more and has higher measured means on the two equivalent-spelling cases. The lower mean on the unequal-integer case does not establish superiority: the short-run confidence intervals overlap. These results do not compare public APIs, parsing, document diffs or end-to-end throughput.

Reproduce with:

```sh
dotnet run -c Release --project benchmarks/JJMatch.Benchmarks -- --filter '*FuzzyValue*' '*NumericExponent*' --job short
dotnet run -c Release --project benchmarks/JJMatch.Benchmarks -- --filter '*NumericComparison*' --job short
```

The CSV reports, including confidence intervals and allocation/GC columns, are retained under [benchmarks/results/2026-10-12](../benchmarks/results/2026-10-12). Full temporary BenchmarkDotNet logs and HTML reports are in `/private/tmp/jjmatch-review-before` and `/private/tmp/jjmatch-review-comparison`. Existing structural benchmark figures in `Match.md` remain historical evidence: notably, 512 distinct shapes per side were reported at about 0.9 GB allocated. They are not presented as new measurements of the current fixtures. A Dry run of all benchmark cases checks setup/execution only and is not used for performance conclusions.

## Fair comparison plan

1. Establish shared semantics before timing. Keep the scalar Numeric comparison above. Add Newtonsoft `JToken.DeepEquals` only for a corpus whose parsed numeric types and values agree exactly; its [contract compares token values and descendants](https://www.newtonsoft.com/json/help/html/M_Newtonsoft_Json_Linq_JToken_DeepEquals.htm). Do not assume duplicate handling or arbitrary-precision decimals are interchangeable.
2. When Strict comparison exists, benchmark a unique-key, ordered-array corpus with identical numeric and string policies against Microsoft and Newtonsoft. Exclude or separately label unsupported duplicate, non-Unicode and extreme-number cases. Compare Boolean results with Boolean results, and full diff generation with equivalent diff generation.
3. Separate parsing, traversal, comparison and full pipeline jobs. Use equal, early-different, late-different, repeated-shape, distinct-shape, duplicate-heavy, Unicode and bounded deep inputs over a documented size range. Verify expected outputs in setup and publish exact package/runtime versions and corpus hashes.
4. Use isolated machines, standard BenchmarkDotNet throughput jobs, multiple launches, sufficient iterations and allocation/GC diagnostics. Compare the same architecture, runtime, GC and build configuration. Cold-process startup needs a separate [ColdStart strategy](https://benchmarkdotnet.org/articles/guides/choosing-run-strategy.html), not the first invocation of a warm throughput run.
5. Report distributions and confidence intervals, throughput, allocated bytes, collection counts and process peak memory separately. Allocation per operation is not retained heap size. Include full raw output and explain outliers or failures. Re-run after each optimization; retain only reproducible gains without semantic regressions.

## Minimal implementation plan and approvals

| Step | Risk and impact | Action |
| --- | --- | --- |
| Completed | Low runtime risk, high validation value | Independent oracles; fixture consolidation with unchanged expectations; synchronized TXT fixtures; honest docs; focused benchmarks. |
| Next decision | High product impact, public API design | Add a separate Boolean structural comparison entry point and public immutable structural result/difference model, without percentage scoring. Keep the current `Match` signature until a deliberate compatibility decision. Initially expose only implemented StructureOnly semantics; do not accept unimplemented modes silently. |
| Same API decision | Behavioral and reliability impact | Define accepted roots, explicit stringified-input policy, argument/parse errors, maximum depth/bytes/nodes/group size and cancellation behavior. Budget exhaustion must be reported as failure, never as a match. |
| Separate feature work | Medium to high semantic impact | Specify and implement Strict and Unordered, then value-based pairing. For Unordered, distinguish existence of a valid bijection from best partial pairing. Do not derive grouping from nontransitive Fuzzy similarity. |
| After explicit optimization approval | Low to medium implementation risk | Benchmark the equal-folded-string shortcut first; then consider cost-only structural comparison and shape caching. Preserve all pair/tie/difference behavior and compare before/after measurements. |
| Later decision | High semantic impact | Define scoring, weights, denominator, missing-node treatment and alignment objective. `MatchedCount` alone is not an approved global similarity formula. |

The proposed public structural operation should return `IsMatch` and structural differences, with occurrence-aware locations or clearly documented display paths. Existing wildcard paths and node references suffice internally but cannot uniquely identify duplicate properties or array occurrences after serialization. Final names, input policy, limits and error types need approval before this public surface is implemented.
