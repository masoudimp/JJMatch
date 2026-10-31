# MatchValues

Fixture paths and counts below are relative to the `core` document unless explicitly identified as `stress`. Both sections live in the approved JSON pair; selecting them preserves the original test scenarios.

`ValueMatcher.MatchValues` compares the primitive values of two `JsonStructureNode`s built by [`Traverse`](Traverse.md) under a `JJValueMatchingMode` and returns whether they are equal. Identical values are always equal; the rules `Normalized`, `Fuzzy` and `Numeric` each accept further pairs.

## Purpose

Decides whether two leaf values are the same value, by the rules the caller selects. It does not compare structure, pair arrays, score, or report differences. [`StructureMatcher.Match`](Match.md) calls it for value-aware primitive comparison, including speculative assignment pairs. `JJMatcher.Match` exposes the rules through independent `JJMatchSettings.StringComparison` and `NumberComparison` properties. They share state with the supported legacy `ValueMode` flags described below; setting one category preserves the other. Fuzzy already includes normalization, so callers choose one string rule. The legacy structural-only overload does not call it.

## Input and output

- `JsonStructureNode left`, `JsonStructureNode right`: the two nodes. Only `ValueKind` and `Value` are read; names, children and the parents play no part.
- `JJValueMatchingMode mode`: the rules to apply. Identity always applies. Each flag is one further rule, and the values are equal when they are identical or any rule that is set holds, so flags combine with `|`. `None` holds no flag and accepts identical values only.
- Optional `double fuzzyThreshold`: the least similarity `Fuzzy` accepts, from 0 to 1. It overrides every other source of the threshold; omitted, it is `ValueMatcher.DefaultFuzzyThreshold` (0.8). The public pipeline passes `JJMatchSettings.FuzzyThreshold`, validated there; this internal parameter is not validated.
- Optional internal `ComparisonBudget`: the public pipeline supplies its per-call allowance and cancellation token. Raw value lengths are charged; fuzzy distance reserves the folded length product and polls cancellation per row; Numeric reserves the sum of squared token lengths conservatively before arbitrary-precision conversion. Internal legacy callers can omit the budget.
- Returns `bool`. Neither node is changed.

`ValueMatcher.Normalize(string)` is the normalization the `Normalized` rule uses, kept separate so that later modes can reuse it. It returns the normalized text and never changes its input.

## Behavior

### Identity (always applied)

This rule is not a mode and cannot be turned off: it holds under every `JJValueMatchingMode`, `None` included. Two values are equal when they are primitives of the same JSON kind and their values are identical. Nothing is normalized, trimmed, case-folded or converted.

- **Strings.** The decoded texts are compared with `StringComparison.Ordinal`, UTF-16 unit by unit: `"Masoud"` and `"masoud"` differ, `" Hello "` and `"Hello"` differ, and a precomposed `é` differs from `e` followed by a combining acute accent. How the text was written in the document plays no part: a string spelled with `\u` escapes equals the same string spelled literally, because both decode to the same text.
- **Numbers.** The raw texts are compared the same way, so a number equals only a number written identically: `123` equals `123`, but `123` and `123.0`, `1e2` and `100`, `1.0` and `1` differ. Quantities are for `Numeric`.
- **Booleans and null.** `true` equals `true`, `false` equals `false` and `null` equals `null`; none equals any other value.
- **Different kinds.** Two values of different JSON kinds are never equal: `"123"` against `123`, `"true"` against `true`, `"null"` against `null`, `null` against `false`.
- **Containers.** Objects and arrays are not primitives and never match under this rule, even an object against itself. Comparing their contents is structural matching's job.
- **Lone surrogates.** A string holding a lone surrogate is compared as it is, without throwing: `"\uD800"` equals `"\uD800"` and differs from `"\uDC00"`, and padding or case around the surrogate still counts.
- **Undecodable strings.** A `String` node whose `Value` is `null` equals nothing, itself and a JSON `null` included: an absent text is not a null value.

### `Normalized`

Two values are equal when both are strings with a `Value` and their normalized texts are equal ignoring case.

- **Trim.** Leading and trailing white space is removed, as `string.Trim` defines it (`char.IsWhiteSpace`: spaces, tabs, line breaks, the no-break space and the other Unicode space separators; not the zero-width space, which is a format character).
- **NFC.** The trimmed text is put into Unicode Normalization Form C, so a precomposed `é` and `e` followed by a combining acute accent are one text.
- **Case.** The two normalized texts are compared with `StringComparison.OrdinalIgnoreCase`: the invariant simple case mapping, character by character, whatever the current culture. `"i"` and `"I"` are equal under a Turkish culture too; the dotted capital `İ` (U+0130) and the dotless small `ı` (U+0131) are letters of their own and equal neither `i` nor `I`. One-to-many mappings are not applied, so `ß` and `SS` differ.
- **Kept.** Internal white space, punctuation and every other character stay as they are: `"hello world"` and `"helloworld"` differ, and so do `"Hello!"` and `"hello"`.
- **No coercion.** The rule applies to two strings only. `"123"` against `123`, `"true"` against `true` and `"null"` against `null` are not equal, whatever the text. The rule says nothing about two numbers, Booleans or nulls either: identity decides those, so equal ones match and no others do.
- **Empty strings.** A string that is empty or holds only white space normalizes to the empty text, so all such strings are equal to each other and to nothing else.
- **Lone surrogates and noncharacters.** A string holding a surrogate that is not half of a pair is not Unicode text and `string.Normalize` rejects it, as it rejects the noncharacters U+FFFE and U+FFFF, so such a string is only trimmed, and then compared like any other. Trimming and case still apply around the lone surrogate: `"x\uD800y"` equals `" X\uD800Y "`.
- **Undecodable strings.** A `String` node whose `Value` is `null` equals nothing, itself included.

### `Fuzzy`

Two values are equal when both are strings with a `Value` and their folded texts are a few edits apart.

- **Fold.** Each text is normalized as `Normalized` does (trimmed, then NFC; a string holding a lone surrogate is only trimmed) and then put into the invariant upper case with `string.ToUpperInvariant`, which uses invariant casing, including supplementary code points; distance is then measured over the resulting UTF-16 units. Case, padding and Unicode form are therefore ignored before any edit is counted: `"  jon smith "` is one edit from `"John Smith"`, and `"Hélène"` written with combining accents is no edit from the precomposed `"Hélène"`, although as UTF-16 units the two differ by four.
- **Distance.** The optimal string alignment distance between the folded texts: the fewest insertions, deletions, substitutions and transpositions of two adjacent units that turn one into the other, where no substring is edited twice. A transposition is one edit, so `"recieve"` is one edit from `"receive"`, not two. The units are UTF-16 code units, so a surrogate pair is two units and an accent in NFC that has no precomposed form is a unit of its own.
- **Threshold.** The pair is equal when its similarity, one minus the distance divided by the length of the longer folded text, is at least the threshold. The threshold comes from three places, each overriding the one before: the constant `ValueMatcher.DefaultFuzzyThreshold` (0.8), `JJMatchSettings.FuzzyThreshold`, which the public API passes down, and the `fuzzyThreshold` parameter of `MatchValues`, which decides whenever it is given. At 0.8 one edit is allowed for every five units: `"Jon Smith"` and `"John Smith"` are equal (one edit in ten), `"Smith"` and `"Smyth"` are equal (one in five, exactly at the limit), `"Jonathon Smyth"` and `"Jonathan Smith"` are equal (two in fourteen); `"Jon"` and `"John"` are not (one in four, a similarity of 0.75, so they are equal at a threshold of 0.75), nor are `"apple"` and `"orange"` (five in six), `"Hélène"` and `"Helene"` (two in six: an accented letter is a different unit) or `"John Smith"` and `"Smith John"` (ten in ten: word order is for `Token`). The allowance is computed as the length minus the units that must remain, `ceiling(threshold × length)`, so a threshold written in decimal keeps its exact boundary although `1 − 0.8` is below `0.2` in binary. At 1 only texts that fold to one text are equal, which is what `Normalized` accepts; at 0 any two strings are equal.
- **Empty strings.** Two texts that fold to the empty text are equal (no edit, length zero). The empty text equals no other text below a threshold of 0: every unit of the other is an edit, and `n` edits in `n` units is a similarity of zero.
- **Not an equivalence relation.** Fuzzy similarity is not transitive: the fixture strings `Jhon Smiht` and `Jon Smith` match, and `Jon Smith` and `Jon Smyth` match, but `Jhon Smiht` and `Jon Smyth` do not. Do not use it as hash-set equality or to collapse array shapes. Optimal string alignment is restricted Damerau distance; it is not unrestricted Damerau–Levenshtein distance and need not satisfy the triangle inequality. Changing the distance, its UTF-16 unit, threshold or digit policy would change semantics.
- **Contains `Normalized`.** Two texts equal under `Normalized` are at distance zero, so whatever `Normalized` accepts, `Fuzzy` accepts: the fixtures are checked pair by pair for this.
- **No coercion.** The rule applies to two strings only. `"123"` against `123`, `"true"` against `true` and `"null"` against `null` are not equal, and two numbers, Booleans or nulls are not decided by it: identity decides those. Objects and arrays never match.
- **Lone surrogates.** A lone surrogate is folded as it is (`ToUpperInvariant` leaves it alone) and compared as a unit like any other, without throwing: `"x\uD800y"` equals `" X\uD800Y "`, and the high surrogate `"\uD800"` is one edit, in one unit, from the low surrogate `"\uDC00"`, so they are not equal. NFC is disabled by a lone surrogate under `Fuzzy` as under `Normalized`, so `"café\uD800"` precomposed and decomposed are two edits in six and not equal.
- **Undecodable strings.** A `String` node whose `Value` is `null` equals nothing, itself included.
- **Cost.** The lengths are compared first: when they differ by more than the allowed edits the pair is rejected without computing anything, and two equal folded texts are accepted without computing anything. Otherwise the distance table is filled three rows at a time (the row being filled and the two before it, for transpositions), and only within the band of cells at most the allowed edits away from the diagonal: a cell outside the band needs more insertions or deletions than allowed, so it cannot take part in a distance within the limit, and the cells just outside the band are set above the limit before they are read. The search stops as soon as every cell of a row is above the allowed edits, which is sound because no cell is smaller than the smallest of the row before it. The table therefore returns the exact distance whenever it is within the limit and some value above it otherwise, which is all the rule reads. The worst case is the longer length times twice the allowed edits plus one, for two long texts of nearly equal length that are nearly equal. Within a public comparison the three rows are reused across pairs, and the normalized and folded texts of a string are kept across the candidates of a group.

### `Numeric`

Two values are equal when both are numbers that denote one quantity, whatever their spelling. The comparison is exact: no tolerance, no rounding and no floating-point or decimal arithmetic.

- **Form.** Each raw text is reduced to its sign, its significant digits (without leading or trailing zeros) and the power of ten they are scaled by, so `123`, `123.0`, `1.23e2` and `12300E-2` all become `123 × 10^0`. Two numbers are equal exactly when the three parts are equal: `123` equals `123.0`, `1e2` equals `100`, `1E+3` equals `1000.000`, `1.5` equals `1.50`, `0.1` equals `0.10` and `-1.5e3` equals `-1500`.
- **Zero.** Zero has no sign and no scale, so `-0`, `0`, `0.0`, `-0.00e+5` and `0e5` are all one value, and none equals a nonzero number, however small: `1e-100` is not `0`.
- **Precision.** The digits are compared as text, so no digit is ever lost: `9007199254740992` and `9007199254740993` differ although a `double` cannot tell them apart, and the two 30-digit integers of `primitiveValues` differ in their last digit. The exponent is read into a `BigInteger`, so `1e400` (beyond `double`), `79228162514264337593543950336` (beyond `decimal`) and `1e99999999999999999999` (beyond `long`) are read exactly and equal their other spellings (`10E399`, `7.9228162514264337593543950336E+28`, `0.1e100000000000000000000`). `1e100` and `1e101` differ.
- **Cost.** Mantissa processing scans and copies text in linear time. The exponent is converted to `BigInteger`, whose parsing cost depends on the runtime and number of exponent digits and is not guaranteed linear. Nothing is raised to the represented power, but very long exponents still consume time and memory; magnitude independence does not imply bounded resource use. See the reproducible measurements in [TechnicalReview.md](TechnicalReview.md).
- **Syntax.** The texts are valid JSON numbers, guaranteed by the parser (an optional minus, an integer part, an optional fraction, an optional exponent with an optional sign and any number of digits), so nothing is validated again. The exponent's case plays no part.
- **No coercion.** The rule applies to two numbers only: `"123"` against `123`, `true` against `1` and `null` against `0` are not equal. It says nothing about two strings, Booleans or nulls: identity decides those. Objects and arrays never match.
- **Unchanged nodes.** The raw texts stay on the nodes as written, so identity still tells `1E+3` from `1000.000` after a `Numeric` comparison.

### Combining modes

`MatchValues` applies identity first and then tests each flag of the mode and applies its rule; the result is `true` as soon as one holds. Every mode is a flag of its own (`None` is `0`, `Normalized` is `1`, `Fuzzy` is `2` and `Numeric` is `4`). `Normalized` accepts `"Masoud"` against `"masoud"` (by its rule) and `123` against `123` and `"123"` against `"123"` (by identity); `"hello world"` against `"helloworld"` and `"123"` against `123` are accepted by neither. `Normalized | Numeric` adds `123` against `123.0` (by `Numeric` only) and still rejects `1e100` against `1e101`, and `Normalized | Fuzzy | Numeric` adds `"Jon Smith"` against `"John Smith"` (by `Fuzzy` only) and still rejects `"Jon"` against `"John"`. The order of the flags changes nothing. `None` is what an unset mode holds and the public default: identical values only.

## Edge cases and limitations

- The result is symmetric and deterministic: it is the same with the sides swapped and on every call, for every pair of primitives in the fixtures.
- The values on the nodes are never changed; normalization produces a new text when it has to. `Trim`, `Normalize` and `IsNormalized` return the input itself when nothing changes, so a trimmed ASCII or already composed value allocates nothing. `Normalized` makes no upper-case or lower-case copy: its case-insensitive comparison works on the normalized texts. `Fuzzy` does create upper-case copies when casing changes.
- Normalization is applied only to a string that `string.Normalize` accepts (well-formed UTF-16 without U+FFFE or U+FFFF), found by one pass over the trimmed text. A lone surrogate or either noncharacter therefore disables NFC for the whole string: `"café\uD800"` and `"café\uD800"` are not equal. Accepted as is: such a string is malformed and rare, and normalizing the well-formed parts around the surrogate was not worth the code.
- NFC uses the runtime's Unicode data (ICU on Linux and macOS, the system tables on Windows), so a runtime without normalization data cannot run this rule. Compatibility characters are not folded: NFC keeps `ﬁ` apart from `fi`.
- `OrdinalIgnoreCase` is a simple case mapping: `ß` and `ẞ`, or `σ` and `ς`, follow that mapping and nothing more.
- `Normalized` and `Fuzzy` compare strings only, `Numeric` numbers only, and identity primitives only.
- `Fuzzy` folds with `ToUpperInvariant` while `Normalized` compares with `OrdinalIgnoreCase`; the two are the invariant simple case mapping on every runtime this library is tested on, and the fixture-wide test checks that every pair `Normalized` accepts, `Fuzzy` accepts. A runtime where the two mappings differ for some character could accept a pair under `Fuzzy` at distance zero that `Normalized` rejects; the reverse cannot happen.
- `Fuzzy` counts UTF-16 units, so a typo in a supplementary character (an emoji, a character outside the Basic Multilingual Plane) is two edits, and a short text tolerates no edit at all: `"Jon"` and `"John"` differ. Whether the threshold should scale differently for short texts, whether accents should be folded away, and whether digits should be matched fuzzily at all (`"SKU-10000"` equals `"SKU-10001"`, one edit in nine) are open; the measure, the default threshold and the performance of the distance are the parts marked for review. The threshold itself is a setting now, so a caller can lower it for short texts.
- `Fuzzy` fills a band of the table about two fifths as wide as the shorter text, so two near-equal long texts cost about two fifths of the full table in time, with memory linear in the shorter text. Outside a public comparison it folds both texts on every call, allocating two strings. Numbers stay as written under identity and `Normalized` (`1.0` against `1`, and `-0` against `0`, are for `Numeric`), and nothing is coerced across JSON kinds.
- `Numeric` has no tolerance: `0.1` and `0.1000000001` differ, as do `1e100` and `1e101`. A tolerant comparison, if ever wanted, would be a parameter of a later mode, not a change to this one.
- `Numeric` reads the exponent into a `BigInteger`, which costs an allocation proportional to the exponent's length; an exponent of thousands of digits is still read exactly. The significant digits are not converted at all. Nothing in the rule depends on the magnitude of the number, so no input can make it compute a large power or run out of memory beyond the size of the texts themselves.
- Identity reads a number's raw text, so two spellings of one quantity differ even when they are equal under every numeric type, and a difference in the exponent's case (`1E2` against `1e2`) is a difference too. Accepted: the rule is defined as identity of the written value.
- The public API rejects undefined flag bits. `None` is valid there and is the default. Public limits can reject expensive valid inputs before this rule completes.

## Tests

`tests/JJMatch.Tests/Matching/ValueMatcherTests.cs`. Every input is a section of `Left.json` and `Right.json`, mostly the `normalizedValues` section, whose properties hold a pair of values on the two sides; some tests compare two different properties with each other.

- case differences ignored (`casing`);
- leading and trailing white space ignored (`padding`);
- precomposed and decomposed forms equal (`composed`);
- internal white space and punctuation kept (`innerSpace`, `punctuated`, and `punctuated` against `padding`);
- empty and white-space-only strings equal to each other and to nothing else (`emptyText`, `blankText`, the top-level `comment`);
- different JSON types never equal, and two equal numbers, Booleans or nulls not decided by `Normalized` (`numberText` against `numberValue`, `booleanText` against `booleanValue`, `nullText` against `nullValue`);
- culture independence under `tr-TR` and the invariant culture, with the premise that Turkish upper-casing changes `i` checked first (`turkish`, `dottedCapital`, `dotlessSmall`);
- lone surrogates compared as they are without throwing, nested under lone surrogate names, with trimming and case still applied, and the NFC limitation beside a lone surrogate pinned (`loneSurrogates.values`, `loneSurrogates.nested`, `lonePadded`, `loneComposed`);
- repeated values in arrays compared element by element (`repeatedWords`, `items[0].attributes.options`);
- nested object values (`deep.deeper.greeting`, `customer.name`, `customer['e-mail']`);
- determinism and symmetry over every primitive of both whole documents against every other, under `None` and `Normalized`;
- original values unchanged after matching, normalization producing a new text, and a text that needs no change returned as is (`padding`, `composed`, `casing`);
- `Normalized` applied whenever its flag is set, including alongside the rules for other kinds (`casing`);
- every mode a distinct power of two with `None` as `0` and the default, `Normalized | Fuzzy` holding both flags and neither holding the other;
- `None` accepting identical values and nothing else (`casing`, `customer.name`).

`Fuzzy`, from the `fuzzyValues` section (the same property names on both sides, each holding a pair of strings) and from strings the fixtures already held. The difficult cases are checked against an independent reference: the full optimal string alignment table, every cell kept and no bound applied, over every string of both documents.

- one edit tolerated for every five units of the longer text, at and below the limit (`typo`, `boundary`, `digits`, `twoEdits`, `swappedPairs`, `casing`);
- a transposition counted as one edit (`transposed`, `swappedPairs`);
- a pair needing more edits rejected, including the words in the other order, an accent and the empty text against a word (`shortName`, `manyEdits`, `unrelated`, `wordOrder`, `accented`, `emptyText` against `casing`, `shortName` against `typo`);
- case, padding and Unicode form ignored before edits are counted, under `tr-TR` and the invariant culture, and internal white space and punctuation counted as units (`caseAndPadding`, `composedForms`, `casing`, `padding`, `composed`, `turkish`, `emptyText`, `blankText`, `deep.deeper.greeting`, `innerSpace`, `punctuated`);
- every pair of both whole documents that `Normalized` accepts, accepted;
- other JSON types never equal and never decided by the rule, containers included (`numberText`, `booleanText`, `nullText`, `numberValue`, `booleanValue`, `nullValue`, `integerDecimal`, `scientific`, `deep`, `people`, `people.names`);
- lone surrogates compared as units without throwing, different ones apart, and the NFC limitation beside a lone surrogate pinned (`lonePadded`, `loneSurrogates.values`, `loneComposed`);
- nested object and array values element by element, and against values elsewhere (`people.names`, `typo`, `caseAndPadding`, `wordOrder`);
- the original values unchanged after matching, with identity still telling them apart (`caseAndPadding`, `composedForms`);
- `Normalized | Fuzzy | Numeric` and the pairwise combinations accepting what any one rule accepts, and the other rules unchanged beside `Fuzzy` (`typo`, `integerDecimal`, `booleanValue`, `shortName`, `numberText`, `hugeExponent`);
- determinism and symmetry over every primitive of both whole documents under `Fuzzy` and `Normalized | Fuzzy | Numeric`, and the undecodable string matching nothing under `Fuzzy` too;
- a fixture-wide check: every string of both whole documents against every other, both ways round, agreeing with the reference table, every pair `Normalized` accepts also accepted, at least 20 pairs equal by distance only, and every string unequal to every number, Boolean and null.

`Fuzzy` against the stress documents, in `tests/JJMatch.Tests/StressTestingJsonsTests.cs` (`TestData/StressTestingJsons`): the five catalogued typos accepted both ways round and rejected by `Normalized`, the `Normalized` scenarios and the NFC/NFD pair accepted, the catalogued word-order, substring, pattern and type-coercion scenarios rejected; the lookalike Booleans (`"true"`, `"1"`, `"yes"`, `"Y"`, `"on"`, `"false"`, `"0"`, `"no"`, `"N"`, `"off"`, `""`) all kept apart from each other and from the values beside them; `Normalized` and `Fuzzy` not throwing on the `noncharacters` string or the lone surrogates; and a sweep of every string of both documents against every other (lone surrogates, control characters and the 100-level-deep text included) for symmetry, `Normalized` implying `Fuzzy`, and both outcomes.

`Numeric`, from the `numericValues` section (the same property names on both sides, each holding one quantity written two ways, or two quantities) and from numbers the fixtures already held. The difficult cases are checked against an independent reference first: `decimal` agrees where it can, `double` is shown to be unable to tell the large integers apart, `decimal` to round the small value to zero and to reject the value beyond its range, and `double` to overflow and underflow on the extreme exponents.

- integers against decimals and trailing zeros (`integerDecimal`, `trailingZeros`, `fraction`, `primitiveValues[2]`, `numberValue`, `duplicateKeys.age`);
- scientific notation with either exponent sign and case (`scientific`, `exponentDecimal`, `hugeExponent`, `primitiveValues[1]`);
- positive and negative zero in every form, equal to each other and to no nonzero number (`signedZero`, `zeroForms`, `tiny`, `amounts.values[2]`, `items[0]['discount %']`);
- very large integers that a `double` cannot tell apart (`beyondDouble`, `primitiveValues[0]`);
- a very small nonzero decimal against zero (`tiny`);
- extreme positive and negative exponents beyond `long`, equal only once the digits around the point are taken into account (`extremeExponent`, `extremeNegativeExponent`);
- values outside the range of `double` and of `decimal` (`outsideDouble`, `outsideDecimal`);
- the sign kept (`negative`);
- other JSON types never equal and never decided by the rule (`numberText`, `booleanValue`, `nullValue`, `items[0].quantity`, `deep`, `amounts`);
- nested object and array values element by element (`amounts.values`, `scientific`, `duplicateKeys.age`);
- the original raw texts unchanged after matching (`exponentDecimal`, `signedZero`);
- `Normalized | Numeric` and the pairwise combinations accepting what any one rule accepts, and `Normalized` unchanged beside `Numeric` (`casing`, `integerDecimal`, `booleanValue`, `nullValue`, `numberText`, `hugeExponent`, `innerSpace`, `scientific`);
- determinism and symmetry over every primitive of both whole documents under `Numeric` and `Normalized | Numeric`, and the undecodable string matching nothing under `Numeric` too.
- a fixture-wide check: every number of both whole documents against every other, both ways round, agreeing with an independent reference that brings the two integer mantissas to one power of ten by multiplication (bounded by the digit count, so the extreme exponents are decided without raising ten to them), identical texts always numerically equal, at least 28 pairs equal by quantity only, and every number unequal to every string, Boolean and null.

Identity, all from values the fixtures already hold:

- identical strings equal and a case-only difference not, under a Turkish and the invariant culture (`casing` against itself, `numberText`, `customer.name`, `repeatedWords`, `turkish`, `dottedCapital`, `dotlessSmall`);
- leading and trailing white space kept, empty strings equal to each other only, and the values left untrimmed (`repeatedWords[2]` against `repeatedWords[0]`, `padding`, `punctuated`, `emptyText`, `blankText`, the top-level `comment`);
- numbers by their written form: `123` against `123` and `30` against `30` equal; `1.0` against `1`, `1.5E+10` against `15000000000` and the two 30-digit integers not (`numberValue`, `duplicateKeys.age`, `primitiveValues`);
- `true`, `false` and `null` equal only to themselves (`booleanValue`, `meta.flags.gift`, `meta.flags.priority`, `quotedNames.child['it's valid']`, `nullValue`, `deletedAt`);
- different JSON kinds never equal, objects and arrays never equal (`numberText` against `numberValue`, `booleanText` against `booleanValue`, `nullText` against `nullValue`, `deep`, `repeatedWords`, the whole document);
- no Unicode normalization, and decoding not being normalization (`composed`, `loneComposed`, `primitiveValues[3]` escaped against literal);
- lone surrogates equal only when identical, nested, with padding and case still counted (`loneSurrogates.values`, `loneSurrogates.nested`, `lonePadded`);
- an undecodable string equal to nothing, itself and `null` included, under every implemented mode, produced by damaging the bytes of `loneSurrogateNames.escapedValue` in memory as the `Traverse` tests do;
- nested values and array elements compared like any other, with `Normalized` accepting what identity rejects (`deep.deeper.greeting`, `customer.name`, `items[0].attributes.options`, `repeatedWords`);
- `Normalized` accepting a pair that identity or its rule accepts (`casing`, `composed`, `numberValue`, `booleanValue`, `nullValue`, `numberText`, `innerSpace`).

### Independent review checks

The fuzzy reference table now folds fixture strings with a runtime-based normalization oracle, rather than calling production `Normalize`. Additional checks cover normalization idempotence and the concrete nontransitive fuzzy triple above. All JSON input comes from the approved fixtures; value tests select their `core` sections.
