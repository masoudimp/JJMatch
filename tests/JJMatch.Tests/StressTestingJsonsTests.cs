using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JJMatch.Tests;

// Adversarial sections: TestData/ComplexJson/Left.json and Right.json, under "stress". Every
// difference between the two sections is catalogued in Left.json under "stressChanges", grouped by the JJValueMatchingMode that
// would accept it or by the kind of structural change. These tests pin what the current functions (Traverse and
// StructureMatcher.Match) do with each catalogued scenario. Expected results are written out
// by hand from the two documents. A value scenario is checked on the tree a value matcher would read, on the
// structural result, which a value difference must never affect, and, for the modes ValueMatcher.MatchValues
// implements, on the matcher itself.
public class StressTestingJsonsTests
{
    // The deepArray section is 100 levels deep, above the parser's default MaxDepth of 64 that the library relies on.
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 128 };

    private static readonly JsonElement Left = Load("Left.json");
    private static readonly JsonElement Right = Load("Right.json");

    private static string ReadText(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ComplexJson", fileName));

    private static JsonElement Load(string fileName)
    {
        using var document = JsonDocument.Parse(ReadText(fileName), Options);
        return document.RootElement.GetProperty("stress").Clone();
    }

    private static JsonStructureNode Tree(JsonElement element) => JsonStructureTraverser.Traverse(element);

    private static StructureMatchResult Match(JsonStructureNode left, JsonStructureNode right) =>
        StructureMatcher.Match(left, right);

    private static StructureMatchResult Match(JsonElement left, JsonElement right) => Match(Tree(left), Tree(right));

    private static StructureMatchResult MatchSection(string name) =>
        Match(Left.GetProperty(name), Right.GetProperty(name));

    // The mismatch type and path of each difference, in the order the result holds them.
    private static IEnumerable<(StructureMismatchType Type, string Path)> Differences(StructureMatchResult result) =>
        result.Differences.Select(difference => (difference.MismatchType, difference.Path));

    private static JsonStructureNode Child(JsonStructureNode node, string name) =>
        node.Children.Single(child => child.PropertyName == name);

    private static JsonStructureNode Descend(JsonStructureNode node, params string[] names) =>
        names.Aggregate(node, Child);

    // The n-th root property with that name, in document order: the root declares "" and "$schema" twice, and
    // JsonElement.GetProperty would return only the last one.
    private static JsonElement RootProperty(JsonElement root, string name, int occurrence) =>
        root.EnumerateObject().Where(property => property.Name == name).ElementAt(occurrence).Value;

    // The value of one leaf, reached by property names below the named section, on both sides.
    private static (string? Left, string? Right) Values(string section, params string[] names) =>
        (Descend(Tree(Left.GetProperty(section)), names).Value, Descend(Tree(Right.GetProperty(section)), names).Value);

    private static void AssertFullMatch(int expectedMatchedCount, StructureMatchResult result)
    {
        Assert.True(result.IsMatch);
        Assert.Empty(result.Differences);
        Assert.Equal(expectedMatchedCount, result.MatchedCount);
    }

    private static double Double(string? text) => double.Parse(text!, CultureInfo.InvariantCulture);

    private static BigInteger Integer(string? text) => BigInteger.Parse(text!, CultureInfo.InvariantCulture);

    // ---- The catalogue ----------------------------------------------------------------------------------------

    [Fact]
    public void Change_catalogue_is_in_sync_with_the_two_documents()
    {
        // The catalogue quotes, for every change, the text as it stands in each stress section.
        using var catalogue = JsonDocument.Parse(ReadText("Left.json"), Options);
        var leftText = Left.GetRawText();
        var rightText = Right.GetRawText();

        var entries = catalogue.RootElement.GetProperty("stressChanges").EnumerateArray().ToList();
        Assert.Equal(51, entries.Count);

        foreach (var entry in entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("category").GetString()));
            Assert.Contains(entry.GetProperty("inA").GetString()!, leftText, StringComparison.Ordinal);
            Assert.Contains(entry.GetProperty("inB").GetString()!, rightText, StringComparison.Ordinal);
        }
    }

    // ---- Whole documents ---------------------------------------------------------------------------------------

    // Every structural change in the catalogue, in the order the matcher reports them: left document order within
    // each object, with the right side's extra properties after the left side's. Nothing in the value categories
    // appears here. The keys section declares the property "\uD800", a lone surrogate, which Traverse keeps as a name
    // of its own.
    [Fact]
    public void Whole_documents_report_exactly_the_catalogued_structural_changes()
    {
        var result = Match(Left, Right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.TypeMismatch, "$.numbers.zero"),
                (StructureMismatchType.MissingProperty, "$.numbers.uint64Max"),
                (StructureMismatchType.TypeMismatch, "$.numbers.int32Max"),
                (StructureMismatchType.ExtraProperty, "$.numbers.UInt64Max"),
                (StructureMismatchType.MissingProperty, "$.strings.delete"),
                (StructureMismatchType.TypeMismatch, "$.keys.__proto__.polluted"),
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.duplicates.interleaved.a"),
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.duplicates.interleaved.b"),
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.duplicates.interleaved.a"),
                (StructureMismatchType.TypeMismatch, "$.containers.emptyObject"),
                (StructureMismatchType.MissingProperty, "$.containers.emptyArray"),
                (StructureMismatchType.TypeMismatch, "$.containers.allNulls"),
                (StructureMismatchType.MissingProperty, "$.containers.objectOfNulls.b"),
                (StructureMismatchType.MissingArrayElementShape, "$.containers.arrayOrScalar[*]"),
                (StructureMismatchType.TypeMismatch, "$.containers.indexLikeObject.length"),
                (StructureMismatchType.MissingArrayElementShape, "$.containers.matrix3d[*]"),
                (StructureMismatchType.ExtraProperty, "$.containers.empty_array"),
                (StructureMismatchType.MissingArrayElementShape, "$.polymorphism[*]"),
                (StructureMismatchType.TypeMismatch, "$.deepObject" + string.Concat(Enumerable.Repeat(".a", 25))),
                (StructureMismatchType.ExtraProperty, "$.addedInB"),
            ],
            Differences(result));

        // The root, 10 root primitives, and the sections: @context 3, numbers 39, strings 52, keys 71, duplicates 40,
        // containers 138, recursiveNames 31, selfReference 26, polymorphism 31, deepArray 101, deepObject 25,
        // deepAlternating 27, whitespace 12, lookalikeValues 23 and last 7.
        Assert.Equal(637, result.MatchedCount);
    }

    [Fact]
    public void Every_section_keeps_the_matched_count_invariants()
    {
        foreach (var property in Left.EnumerateObject())
        {
            var left = Tree(property.Value);
            var right = Tree(Right.GetProperty(property.Name));

            var result = Match(left, right);
            var swapped = Match(right, left);
            var leftSize = Match(left, left).MatchedCount;
            var rightSize = Match(right, right).MatchedCount;

            Assert.Equal(result.MatchedCount, swapped.MatchedCount);
            Assert.True(result.MatchedCount <= Math.Min(leftSize, rightSize));
            Assert.Equal(result.IsMatch, result.MatchedCount == leftSize && result.MatchedCount == rightSize);
        }
    }

    // ---- Normalized: case and whitespace -----------------------------------------------------------------------

    [Fact]
    public void Normalized_differences_are_string_leaves_that_structure_ignores()
    {
        Assert.Equal(("single space key", "  Single   SPACE key "),
            (Tree(RootProperty(Left, " ", 0)).Value, Tree(RootProperty(Right, " ", 0)).Value));
        Assert.Equal(("leaf", " LEAF"), Values("recursiveNames", "node", "node", "node", "node", "value"));

        var last = (Tree(Left.GetProperty("last")), Tree(Right.GetProperty("last")));
        Assert.Equal(("end", "End "), (LastLeaf(last.Item1).Value, LastLeaf(last.Item2).Value));

        AssertFullMatch(1, Match(RootProperty(Left, " ", 0), RootProperty(Right, " ", 0)));
        AssertFullMatch(31, MatchSection("recursiveNames"));
        // Three arrays, three objects and the string.
        AssertFullMatch(7, Match(last.Item1, last.Item2));

        static JsonStructureNode LastLeaf(JsonStructureNode array) =>
            Child(Child(Child(array.Children[0], "last").Children[0], "last").Children[0], "last");
    }

    // ---- Fuzzy: typos, one of them 100 levels down -------------------------------------------------------------

    [Fact]
    public void Fuzzy_differences_are_string_leaves_that_structure_ignores()
    {
        Assert.Equal(("empty key at root", "emtpy key at root"),
            (Tree(RootProperty(Left, "", 0)).Value, Tree(RootProperty(Right, "", 0)).Value));
        Assert.Equal(("tab key", "tab kee"),
            (Tree(Left.GetProperty("\t")).Value, Tree(Right.GetProperty("\t")).Value));
        Assert.Equal(("siblings ignored or merged?", "siblings ignord or merged"),
            Values("selfReference", "refWithSiblings", "description"));
        Assert.Equal(("note the trailing space in the key", "note the trailling space in the key"),
            (Tree(Left.GetProperty("numbers ")).Value, Tree(Right.GetProperty("numbers ")).Value));

        var deepLeft = Tree(Left.GetProperty("deepArray"));
        var deepRight = Tree(Right.GetProperty("deepArray"));
        var (leftLeaf, leftDepth) = Bottom(deepLeft);
        var (rightLeaf, rightDepth) = Bottom(deepRight);
        Assert.Equal(100, leftDepth);
        Assert.Equal(100, rightDepth);
        Assert.Equal(("100 levels down", "100 levels dwon"), (leftLeaf.Value, rightLeaf.Value));

        AssertFullMatch(26, MatchSection("selfReference"));
        // 100 nested arrays and the string.
        AssertFullMatch(101, Match(deepLeft, deepRight));

        static (JsonStructureNode Leaf, int Depth) Bottom(JsonStructureNode node)
        {
            var depth = 0;
            while (node.ValueKind != JsonValueKind.String)
            {
                node = Assert.Single(node.Children);
                depth++;
            }
            return (node, depth);
        }
    }

    private static bool MatchesUnder(JJValueMatchingMode mode, JsonStructureNode left, JsonStructureNode right) =>
        ValueMatcher.MatchValues(left, right, mode);

    // Every primitive node of a tree, in document order.
    private static IEnumerable<JsonStructureNode> Leaves(JsonStructureNode node) =>
        node.Children.Count == 0 && node.ValueKind != JsonValueKind.Object && node.ValueKind != JsonValueKind.Array
            ? [node]
            : node.Children.SelectMany(Leaves);

    [Fact]
    public void Fuzzy_accepts_every_catalogued_typo_and_nothing_catalogued_as_another_string_change()
    {
        var typos = new List<(JsonStructureNode Left, JsonStructureNode Right)>
        {
            (Tree(RootProperty(Left, "", 0)), Tree(RootProperty(Right, "", 0))),
            (Tree(Left.GetProperty("\t")), Tree(Right.GetProperty("\t"))),
            (Descend(Tree(Left.GetProperty("selfReference")), "refWithSiblings", "description"), Descend(Tree(Right.GetProperty("selfReference")), "refWithSiblings", "description")),
            (Tree(Left.GetProperty("numbers ")), Tree(Right.GetProperty("numbers "))),
        };
        var deepLeft = Tree(Left.GetProperty("deepArray"));
        var deepRight = Tree(Right.GetProperty("deepArray"));
        while (deepLeft.ValueKind != JsonValueKind.String)
        {
            deepLeft = Assert.Single(deepLeft.Children);
            deepRight = Assert.Single(deepRight.Children);
        }
        typos.Add((deepLeft, deepRight));

        // The five Fuzzy scenarios: a swapped pair, a changed letter, a dropped letter beside a dropped question
        // mark, a doubled letter, and a swapped pair 100 levels down. Each is within one edit per five units and is
        // accepted by Fuzzy alone, both ways round; Exact and Normalized reject every one.
        Assert.Equal(
            [
                ("empty key at root", "emtpy key at root"),
                ("tab key", "tab kee"),
                ("siblings ignored or merged?", "siblings ignord or merged"),
                ("note the trailing space in the key", "note the trailling space in the key"),
                ("100 levels down", "100 levels dwon"),
            ],
            typos.Select(pair => (pair.Left.Value, pair.Right.Value)));
        foreach (var (left, right) in typos)
        {
            Assert.True(MatchesUnder(JJValueMatchingMode.Fuzzy, left, right), left.Value);
            Assert.True(MatchesUnder(JJValueMatchingMode.Fuzzy, right, left), left.Value);
            Assert.False(MatchesUnder(JJValueMatchingMode.Normalized, left, right), left.Value);
        }

        // The three Normalized scenarios are no edit apart, so Fuzzy accepts them too.
        Assert.True(MatchesUnder(JJValueMatchingMode.Fuzzy, Tree(RootProperty(Left, " ", 0)), Tree(RootProperty(Right, " ", 0))));
        Assert.True(MatchesUnder(JJValueMatchingMode.Fuzzy, Descend(Tree(Left.GetProperty("recursiveNames")), "node", "node", "node", "node", "value"), Descend(Tree(Right.GetProperty("recursiveNames")), "node", "node", "node", "node", "value")));
        var hexLeft = Child(Tree(Left.GetProperty("strings")), "hexCaseVariants").Children;
        var hexRight = Child(Tree(Right.GetProperty("strings")), "hexCaseVariants").Children;
        Assert.NotEqual(hexLeft[2].Value, hexRight[2].Value);
        Assert.True(MatchesUnder(JJValueMatchingMode.Fuzzy, hexLeft[2], hexRight[2]));

        // The Token, Contains and Pattern scenarios are many edits apart: words in another order, a text and a
        // text it contains, a text and a regular expression it satisfies. Fuzzy rejects them all.
        Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, Tree(Left.GetProperty("Numbers")), Tree(Right.GetProperty("Numbers"))));
        Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, Tree(RootProperty(Left, "$schema", 1)), Tree(RootProperty(Right, "$schema", 1))));
        Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, Descend(Tree(Left.GetProperty("strings")), "commentLookalikes"), Descend(Tree(Right.GetProperty("strings")), "commentLookalikes")));
        Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, Descend(Tree(Left.GetProperty("strings")), "base64"), Descend(Tree(Right.GetProperty("strings")), "base64")));
        Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, Tree(RootProperty(Left, "$schema", 0)), Tree(RootProperty(Right, "$schema", 0))));
        Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, Child(Tree(Left.GetProperty("strings")), "looksLikeDates").Children[1], Child(Tree(Right.GetProperty("strings")), "looksLikeDates").Children[1]));

        // The TypeCoerced scenarios put the same text on both sides as a number and as a string; Fuzzy never
        // crosses kinds.
        var int32 = (Descend(Tree(Left.GetProperty("numbers")), "int32Max"), Descend(Tree(Right.GetProperty("numbers")), "int32Max"));
        Assert.Equal((JsonValueKind.Number, JsonValueKind.String), (int32.Item1.ValueKind, int32.Item2.ValueKind));
        Assert.Equal(int32.Item1.Value, int32.Item2.Value);
        Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, int32.Item1, int32.Item2));
    }

    [Fact]
    public void Normalized_and_fuzzy_do_not_throw_on_noncharacters_or_lone_surrogates()
    {
        // string.Normalize rejects U+FFFE and U+FFFF as well as a surrogate without its partner, so these strings
        // are compared only trimmed. Each is the same on both sides, so every rule accepts it, and the texts that
        // differ stay apart.
        var left = Tree(Left.GetProperty("strings"));
        var right = Tree(Right.GetProperty("strings"));
        Assert.Equal("\uFFFE\uFFFF", Child(left, "noncharacters").Value);
        foreach (var name in new[] { "noncharacters", "loneHighSurrogate", "loneLowSurrogate", "reversedSurrogatePair", "highSurrogateThenLetter" })
        {
            Assert.Equal(Child(left, name).Value, Child(right, name).Value);
            foreach (var mode in new[] { JJValueMatchingMode.None, JJValueMatchingMode.Normalized, JJValueMatchingMode.Fuzzy })
            {
                Assert.True(MatchesUnder(mode, Child(left, name), Child(right, name)), $"{name} under {mode}");
            }
        }
        Assert.False(MatchesUnder(JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy, Child(left, "noncharacters"), Child(right, "loneHighSurrogate")));
        Assert.False(MatchesUnder(JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy, Child(left, "loneHighSurrogate"), Child(right, "loneLowSurrogate")));
    }

    [Fact]
    public void Fuzzy_keeps_the_lookalike_booleans_apart()
    {
        // "true", "1", "yes", "Y", "on", "false", "0", "no", "N", "off", "": short texts that mean the same thing
        // to a lenient reader. None is within one edit per five units of another ("on" against "no" is one swap in
        // two units), so Fuzzy equates none of them, and none equals the true, false, 1 or 0 beside them.
        var booleans = Child(Tree(Left.GetProperty("lookalikeValues")), "booleans").Children;
        var texts = booleans.Where(element => element.ValueKind == JsonValueKind.String).ToList();
        Assert.Equal(["true", "1", "yes", "Y", "on", "false", "0", "no", "N", "off", ""], texts.Select(element => element.Value));
        foreach (var left in texts)
        {
            foreach (var right in booleans)
            {
                Assert.Equal(ReferenceEquals(left, right), MatchesUnder(JJValueMatchingMode.Fuzzy, left, right));
            }
        }
    }

    [Fact]
    public void Fuzzy_is_symmetric_and_contains_normalized_over_every_string_of_both_documents()
    {
        // Every string leaf of both documents against every other, both ways round: lone surrogates, reversed
        // pairs, control characters, a 100-level-deep text and the 30-level-deep ones included. Nothing throws,
        // the result is symmetric, whatever Normalized accepts Fuzzy accepts, and both outcomes occur, with some
        // pairs equal by distance only.
        var leaves = Leaves(Tree(Left)).Concat(Leaves(Tree(Right))).ToList();
        var strings = leaves.Where(leaf => leaf.ValueKind == JsonValueKind.String).ToList();
        var others = leaves.Where(leaf => leaf.ValueKind != JsonValueKind.String).ToList();
        Assert.True(strings.Count >= 300, $"{strings.Count} strings");
        Assert.Contains(strings, leaf => leaf.Value == "\uD800");
        Assert.Contains(strings, leaf => leaf.Value == "100 levels dwon");

        var matched = 0;
        var matchedButNotNormalized = 0;
        foreach (var left in strings)
        {
            foreach (var right in strings)
            {
                var result = MatchesUnder(JJValueMatchingMode.Fuzzy, left, right);
                Assert.Equal(result, MatchesUnder(JJValueMatchingMode.Fuzzy, right, left));
                if (MatchesUnder(JJValueMatchingMode.Normalized, left, right))
                {
                    Assert.True(result, $"'{left.Value}' against '{right.Value}'");
                }
                else if (result)
                {
                    matchedButNotNormalized++;
                }
                if (result)
                {
                    matched++;
                }
            }
            foreach (var other in others)
            {
                Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, left, other));
                Assert.False(MatchesUnder(JJValueMatchingMode.Fuzzy, other, left));
            }
        }
        Assert.True(matched >= strings.Count, $"{matched} matches for {strings.Count} strings");
        Assert.True(matched < strings.Count * strings.Count, "every pair matched");
        Assert.True(matchedButNotNormalized >= 2 * 5, $"{matchedButNotNormalized} pairs equal by distance only");
    }

    // ---- Token: words in another order -------------------------------------------------------------------------

    [Fact]
    public void Token_differences_are_string_leaves_that_structure_ignores()
    {
        Assert.Equal(("case variant of a root key", "root key, a case variant of"),
            (Tree(Left.GetProperty("Numbers")).Value, Tree(Right.GetProperty("Numbers")).Value));
        // The second "$schema" at the root; the first one is the Pattern scenario.
        Assert.Equal(("duplicate of a root key, different value", "different value, duplicate of a root key"),
            (Tree(RootProperty(Left, "$schema", 1)).Value, Tree(RootProperty(Right, "$schema", 1)).Value));

        AssertFullMatch(1, Match(Left.GetProperty("Numbers"), Right.GetProperty("Numbers")));
        AssertFullMatch(1, Match(RootProperty(Left, "$schema", 1), RootProperty(Right, "$schema", 1)));
    }

    // ---- Numeric: the same quantity written differently --------------------------------------------------------

    [Fact]
    public void Numeric_differences_keep_the_raw_text_and_are_equal_as_quantities()
    {
        // Numbers are stored as written, so each pair differs in the tree and is equal only when parsed.
        var pairs = new (string? Left, string? Right)[]
        {
            Values("numbers", "floatThatIsInteger"),
            Values("numbers", "floatLookingInteger"),
            Values("numbers", "oneFloat"),
            Values("numbers", "trailingZeros"),
            Values("recursiveNames", "node", "value"),
            (Child(Tree(Left.GetProperty("containers")), "everyTypeOnce").Children[6].Value,
                Child(Tree(Right.GetProperty("containers")), "everyTypeOnce").Children[6].Value),
        };

        Assert.Equal(
            [
                ("1e3", "1000"),
                ("1000.0", "1e3"),
                ("1.0", "1"),
                ("1.5000000000000000000000000", "1.5"),
                ("1.0", "1"),
                ("1e10", "10000000000"),
            ],
            pairs);
        Assert.All(pairs, pair => Assert.NotEqual(pair.Left, pair.Right));
        Assert.All(pairs, pair => Assert.Equal(Double(pair.Left), Double(pair.Right)));

        // Both sides are Number leaves, so none of them is a structural difference.
        Assert.DoesNotContain("$.floatThatIsInteger", Differences(MatchSection("numbers")).Select(difference => difference.Path));
        Assert.DoesNotContain("$.oneFloat", Differences(MatchSection("numbers")).Select(difference => difference.Path));
        AssertFullMatch(31, MatchSection("recursiveNames"));
    }

    [Fact]
    public void Numeric_traps_are_equal_as_double_but_different_as_integers()
    {
        var maxSafePlusTwo = Values("numbers", "maxSafePlusTwo");
        var hugeInteger = Values("numbers", "hugeInteger");

        Assert.Equal(("9007199254740993", "9007199254740992"), maxSafePlusTwo);
        Assert.Equal(
            ("123456789012345678901234567890123456789012345678901234567890",
                "123456789012345678901234567890123456789012345678901234567891"),
            hugeInteger);

        // A double cannot tell them apart; an arbitrary-precision integer can. Value matching must not use double.
        Assert.Equal(Double(maxSafePlusTwo.Left), Double(maxSafePlusTwo.Right));
        Assert.Equal(Double(hugeInteger.Left), Double(hugeInteger.Right));
        Assert.NotEqual(Integer(maxSafePlusTwo.Left), Integer(maxSafePlusTwo.Right));
        Assert.NotEqual(Integer(hugeInteger.Left), Integer(hugeInteger.Right));
    }

    [Fact]
    public void A_real_value_change_is_different_as_text_and_as_quantity()
    {
        var pointOne = Values("numbers", "pointOne");

        Assert.Equal(("0.1", "0.11"), pointOne);
        Assert.NotEqual(Double(pointOne.Left), Double(pointOne.Right));
        Assert.DoesNotContain("$.pointOne", Differences(MatchSection("numbers")).Select(difference => difference.Path));
    }

    // ---- TypeCoerced: the same content as a number or boolean against a string --------------------------------

    [Fact]
    public void Type_coerced_differences_are_type_mismatches_with_the_same_text_on_both_sides()
    {
        var numbers = MatchSection("numbers");
        var int32Max = numbers.Differences.Single(difference => difference.Path == "$.int32Max");
        Assert.Equal(StructureMismatchType.TypeMismatch, int32Max.MismatchType);
        Assert.Equal((JsonValueKind.Number, JsonValueKind.String), (int32Max.Left!.ValueKind, int32Max.Right!.ValueKind));
        Assert.Equal(("2147483647", "2147483647"), (int32Max.Left.Value, int32Max.Right.Value));

        var containers = MatchSection("containers");
        var length = containers.Differences.Single(difference => difference.Path == "$.indexLikeObject.length");
        Assert.Equal(StructureMismatchType.TypeMismatch, length.MismatchType);
        Assert.Equal((JsonValueKind.Number, JsonValueKind.String), (length.Left!.ValueKind, length.Right!.ValueKind));
        Assert.Equal(("3", "3"), (length.Left.Value, length.Right.Value));

        // true against "true" is the only change in the keys section, whose "\uD800" name is kept as a lone surrogate.
        var keysLeft = Tree(Left.GetProperty("keys"));
        var keys = Match(keysLeft, Tree(Right.GetProperty("keys")));
        Assert.Equal([(StructureMismatchType.TypeMismatch, "$.__proto__.polluted")], Differences(keys));
        Assert.Same(Child(Child(keysLeft, "__proto__"), "polluted"), keys.Differences[0].Left);
        Assert.Equal((JsonValueKind.True, JsonValueKind.String), (keys.Differences[0].Left!.ValueKind, keys.Differences[0].Right!.ValueKind));
        Assert.Equal((null, "true"), (keys.Differences[0].Left!.Value, keys.Differences[0].Right!.Value));
        Assert.Contains(keysLeft.Children, child => child.PropertyName == "\uD800");
        // The root, 68 properties, constructor.prototype and its polluted, less __proto__.polluted.
        Assert.Equal(71, keys.MatchedCount);

        // In an array the coerced element becomes a shape of its own: { type, radius: Number } has no partner, since
        // { type, radius: String } is already paired with the "Circle" element of the same shape.
        var left = Tree(Left.GetProperty("polymorphism"));
        var polymorphism = Match(left, Tree(Right.GetProperty("polymorphism")));
        Assert.Equal([(StructureMismatchType.MissingArrayElementShape, "$[*]")], Differences(polymorphism));
        Assert.Same(left.Children[0], polymorphism.Differences[0].Left);
        Assert.Equal("1", Child(left.Children[0], "radius").Value);
        // The root and eight shapes of 4, 9, 3, 3, 2, 2, 6 and 1 nodes.
        Assert.Equal(31, polymorphism.MatchedCount);
    }

    // ---- Contains, in both directions --------------------------------------------------------------------------

    [Fact]
    public void Contains_differences_are_string_leaves_where_one_side_contains_the_other()
    {
        var comment = Values("strings", "commentLookalikes");
        var base64 = Values("strings", "base64");

        Assert.Equal(("// not a comment /* still not */ # nope", "not a comment"), comment);
        Assert.Equal(("eyJhIjoxfQ==", "prefix eyJhIjoxfQ== suffix"), base64);
        Assert.Contains(comment.Right!, comment.Left!, StringComparison.Ordinal);
        Assert.Contains(base64.Left!, base64.Right!, StringComparison.Ordinal);

        var strings = MatchSection("strings");
        Assert.DoesNotContain("$.commentLookalikes", Differences(strings).Select(difference => difference.Path));
        Assert.DoesNotContain("$.base64", Differences(strings).Select(difference => difference.Path));
    }

    // ---- Pattern: the right side is a regular expression the left side satisfies -------------------------------

    [Fact]
    public void Pattern_differences_are_string_leaves_where_the_right_side_is_a_regex_the_left_satisfies()
    {
        var schema = (Tree(RootProperty(Left, "$schema", 0)).Value, Tree(RootProperty(Right, "$schema", 0)).Value);
        var date = (Child(Tree(Left.GetProperty("strings")), "looksLikeDates").Children[1].Value,
            Child(Tree(Right.GetProperty("strings")), "looksLikeDates").Children[1].Value);

        Assert.Equal(("https://example.com/schemas/torture.json", @"^https://example\.com/schemas/\w+\.json$"), schema);
        Assert.Equal(("2024-03-01T10:15:00Z", @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"), date);
        Assert.Matches(schema.Item2!, schema.Item1!);
        Assert.Matches(date.Item2!, date.Item1!);
        Assert.DoesNotMatch(schema.Item1!, schema.Item2!);

        AssertFullMatch(1, Match(RootProperty(Left, "$schema", 0), RootProperty(Right, "$schema", 0)));
    }

    // ---- Structure: removed, added, renamed, type changed, wrapped, array length, nesting depth ----------------

    [Fact]
    public void Numbers_section_reports_a_type_change_and_a_case_only_rename()
    {
        var left = Tree(Left.GetProperty("numbers"));
        var right = Tree(Right.GetProperty("numbers"));

        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.TypeMismatch, "$.zero"),
                (StructureMismatchType.MissingProperty, "$.uint64Max"),
                (StructureMismatchType.TypeMismatch, "$.int32Max"),
                (StructureMismatchType.ExtraProperty, "$.UInt64Max"),
            ],
            Differences(result));

        // 0 against false: a Number against a Boolean.
        Assert.Equal((JsonValueKind.Number, JsonValueKind.False), (result.Differences[0].Left!.ValueKind, result.Differences[0].Right!.ValueKind));

        // Names are compared case-sensitively, so the rename is a missing and an extra property with equal values.
        Assert.Same(Child(left, "uint64Max"), result.Differences[1].Left);
        Assert.Same(Child(right, "UInt64Max"), result.Differences[3].Right);
        Assert.Equal(result.Differences[1].Left!.Value, result.Differences[3].Right!.Value);

        // The root and 41 properties, less zero, uint64Max and int32Max.
        Assert.Equal(39, result.MatchedCount);
    }

    [Fact]
    public void Removed_property_is_missing_and_everything_else_in_strings_matches()
    {
        var left = Tree(Left.GetProperty("strings"));

        var result = Match(left, Tree(Right.GetProperty("strings")));

        Assert.Equal([(StructureMismatchType.MissingProperty, "$.delete")], Differences(result));
        Assert.Same(Child(left, "delete"), result.Differences[0].Left);
        Assert.Equal("\u007F", result.Differences[0].Left!.Value);
        Assert.Null(result.Differences[0].Right);

        // The root, 43 properties and nine arrays holding one String shape each, less delete.
        Assert.Equal(52, result.MatchedCount);
    }

    [Fact]
    public void Added_property_exists_only_on_the_right()
    {
        Assert.False(Left.TryGetProperty("addedInB", out _));
        Assert.True(Right.TryGetProperty("addedInB", out var added));

        var tree = Tree(added);
        Assert.Equal(["note", "list"], tree.Children.Select(child => child.PropertyName));
        Assert.Equal("only in the second file", Child(tree, "note").Value);
        Assert.Equal(["1", "2", "3"], Child(tree, "list").Children.Select(child => child.Value));
    }

    [Fact]
    public void Containers_section_reports_renames_type_changes_a_missing_null_and_shapes_without_a_partner()
    {
        var left = Tree(Left.GetProperty("containers"));
        var right = Tree(Right.GetProperty("containers"));

        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.TypeMismatch, "$.emptyObject"),
                (StructureMismatchType.MissingProperty, "$.emptyArray"),
                (StructureMismatchType.TypeMismatch, "$.allNulls"),
                (StructureMismatchType.MissingProperty, "$.objectOfNulls.b"),
                (StructureMismatchType.MissingArrayElementShape, "$.arrayOrScalar[*]"),
                (StructureMismatchType.TypeMismatch, "$.indexLikeObject.length"),
                (StructureMismatchType.MissingArrayElementShape, "$.matrix3d[*]"),
                (StructureMismatchType.ExtraProperty, "$.empty_array"),
            ],
            Differences(result));

        // {} against []: two empty containers of different types.
        Assert.Equal((JsonValueKind.Object, JsonValueKind.Array), (result.Differences[0].Left!.ValueKind, result.Differences[0].Right!.ValueKind));

        // emptyArray renamed to empty_array.
        Assert.Same(Child(left, "emptyArray"), result.Differences[1].Left);
        Assert.Same(Child(right, "empty_array"), result.Differences[7].Right);

        // [null, null, null, null] against null: the array's Null shape is not reported on its own.
        Assert.Equal((JsonValueKind.Array, JsonValueKind.Null), (result.Differences[2].Left!.ValueKind, result.Differences[2].Right!.ValueKind));

        // A null property is a property: { a: null, b: null } against { a: null } misses b.
        Assert.Same(Child(Child(left, "objectOfNulls"), "b"), result.Differences[3].Left);
        Assert.Equal(JsonValueKind.Null, result.Differences[3].Left!.ValueKind);

        // { tag: "a" } against { tag: ["a"] }: the right shape already exists on the left, so the scalar shape is left over.
        Assert.Same(Child(left, "arrayOrScalar").Children[0], result.Differences[4].Left);
        Assert.Equal(JsonValueKind.String, Child(result.Differences[4].Left!, "tag").ValueKind);

        // matrix3d lost its empty array, a shape the three other elements do not have.
        Assert.Same(Child(left, "matrix3d").Children[3], result.Differences[6].Left);
        Assert.Empty(result.Differences[6].Left!.Children);

        // Unmatched: emptyObject, emptyArray, allNulls and its Null shape, objectOfNulls.b, { tag: "a" } and its
        // tag, length, and the empty array of matrix3d: nine of the left side's 147 nodes.
        Assert.Equal(138, result.MatchedCount);
    }

    [Fact]
    public void Array_length_changes_without_a_new_shape_are_not_differences()
    {
        var left = Tree(Left.GetProperty("containers"));
        var right = Tree(Right.GetProperty("containers"));

        // [1, null, null, null, 5] against [1, null, 5]: Number and Null on both sides.
        Assert.Equal((5, 3), (Child(left, "sparseLike").Children.Count, Child(right, "sparseLike").Children.Count));
        AssertFullMatch(3, Match(Child(left, "sparseLike"), Child(right, "sparseLike")));

        // jagged gained [7, 8, 9], which has the shape [1] and [1, 2] already have.
        Assert.Equal((5, 6), (Child(left, "jagged").Children.Count, Child(right, "jagged").Children.Count));
        Assert.Equal(["7", "8", "9"], Child(right, "jagged").Children[3].Children.Select(child => child.Value));
        // The root; [Number] (2); []; [Number, [Number, [Number, [Number]]]] (8); [[[[]]]] (4).
        AssertFullMatch(16, Match(Child(left, "jagged"), Child(right, "jagged")));
    }

    [Fact]
    public void Shallower_nesting_is_a_type_mismatch_where_the_object_meets_the_string()
    {
        var left = Tree(Left.GetProperty("deepObject"));
        var right = Tree(Right.GetProperty("deepObject"));
        Assert.Equal(30, Depth(left));
        Assert.Equal(25, Depth(right));

        var result = Match(left, right);

        Assert.Equal(
            [(StructureMismatchType.TypeMismatch, "$" + string.Concat(Enumerable.Repeat(".a", 25)))],
            Differences(result));
        Assert.Equal(JsonValueKind.Object, result.Differences[0].Left!.ValueKind);
        Assert.Equal("30 levels down", result.Differences[0].Right!.Value);
        // The objects at depth 0 to 24.
        Assert.Equal(25, result.MatchedCount);

        static int Depth(JsonStructureNode node)
        {
            var depth = 0;
            while (node.ValueKind == JsonValueKind.Object)
            {
                node = Assert.Single(node.Children);
                depth++;
            }
            return depth;
        }
    }

    // ---- Array order ---------------------------------------------------------------------------------------------

    [Fact]
    public void Array_element_order_is_kept_in_the_tree_and_ignored_by_the_matcher()
    {
        var left = Tree(Left.GetProperty("containers"));
        var right = Tree(Right.GetProperty("containers"));

        // tupleLike reversed: four distinct pair shapes on each side.
        Assert.Equal("id", Child(left, "tupleLike").Children[0].Children[0].Value);
        Assert.Equal("meta", Child(right, "tupleLike").Children[0].Children[0].Value);
        // The root; [String, Number] (3); [String, String] (2); [String, [String]] (4); [String, { k }] (4).
        AssertFullMatch(14, Match(Child(left, "tupleLike"), Child(right, "tupleLike")));

        // Primitives reordered: one String shape.
        var literals = (Child(Tree(Left.GetProperty("strings")), "looksLikeLiterals"), Child(Tree(Right.GetProperty("strings")), "looksLikeLiterals"));
        Assert.Equal(["null", "true", "false", "NaN"], literals.Item1.Children.Take(4).Select(child => child.Value));
        Assert.Equal(["NaN", "false", "true", "null"], literals.Item2.Children.Take(4).Select(child => child.Value));
        AssertFullMatch(2, Match(literals.Item1, literals.Item2));

        // Objects swapped in polymorphism: rect is second on the left and third on the right. The swap adds nothing
        // to the one difference the coerced radius causes.
        var polymorphism = (Tree(Left.GetProperty("polymorphism")), Tree(Right.GetProperty("polymorphism")));
        Assert.Equal("rect", Child(polymorphism.Item1.Children[1], "type").Value);
        Assert.Equal("rect", Child(polymorphism.Item2.Children[2], "type").Value);
        Assert.Equal([(StructureMismatchType.MissingArrayElementShape, "$[*]")], Differences(Match(polymorphism.Item1, polymorphism.Item2)));
    }

    // ---- No real difference --------------------------------------------------------------------------------------

    [Fact]
    public void Property_order_does_not_change_the_match()
    {
        AssertFullMatch(3, MatchSection("@context"));
        AssertFullMatch(4, Match(Left.GetProperty("selfReference").GetProperty("anchors"), Right.GetProperty("selfReference").GetProperty("anchors")));
    }

    [Fact]
    public void Whitespace_between_tokens_is_invisible_after_parsing()
    {
        var left = Left.GetProperty("whitespace").GetProperty("noSpaces");
        var right = Right.GetProperty("whitespace").GetProperty("noSpaces");

        // The root, a, Number, { b } and b.
        AssertFullMatch(5, Match(left, right));
        AssertFullMatch(12, MatchSection("whitespace"));
    }

    [Fact]
    public void Escaped_and_literal_text_decode_to_the_same_value()
    {
        var surrogatePair = Values("strings", "validSurrogatePair");

        Assert.Equal("😀", surrogatePair.Left);
        Assert.Equal(surrogatePair.Left, surrogatePair.Right);

        // The malformed escapes of the section are decoded unit by unit, not dropped: a lone high and a lone low
        // surrogate, a pair in the wrong order, and a high surrogate followed by a letter. Identical on both sides.
        foreach (var name in new[] { "loneHighSurrogate", "loneLowSurrogate", "reversedSurrogatePair", "highSurrogateThenLetter" })
        {
            var values = Values("strings", name);
            Assert.NotNull(values.Left);
            Assert.Equal(values.Left, values.Right);
        }
        Assert.Equal("\uD800", Values("strings", "loneHighSurrogate").Left);
        Assert.Equal("\uDFFF", Values("strings", "loneLowSurrogate").Left);
        Assert.Equal("\uDE00\uD83D", Values("strings", "reversedSurrogatePair").Left);
        Assert.Equal("\uD83Dx", Values("strings", "highSurrogateThenLetter").Left);
    }

    // ---- Duplicate keys ------------------------------------------------------------------------------------------

    [Fact]
    public void Duplicate_keys_are_kept_in_document_order_and_paired_by_shape()
    {
        var left = Tree(Left.GetProperty("duplicates"));
        var right = Tree(Right.GetProperty("duplicates"));

        // { k: 1, k: 2, k: 3 } against { k: 3, k: 2, k: 1 }: a consumer keeping the last value reads 3 against 1,
        // but every occurrence is a Number, so it is a value difference, not a structural one.
        Assert.Equal(["1", "2", "3"], Child(left, "simple").Children.Select(child => child.Value));
        Assert.Equal(["3", "2", "1"], Child(right, "simple").Children.Select(child => child.Value));
        Assert.All(Child(right, "simple").Children, child => Assert.Equal("k", child.PropertyName));
        AssertFullMatch(4, Match(Child(left, "simple"), Child(right, "simple")));

        // { a: 1, b: 2, a: 3, b: 4, a: 5 } against { a: 5, b: 4 }: the first occurrences pair up and the rest are surplus.
        Assert.Equal(["a", "b", "a", "b", "a"], Child(left, "interleaved").Children.Select(child => child.PropertyName));
        Assert.Equal([("a", "5"), ("b", "4")], Child(right, "interleaved").Children.Select(child => (child.PropertyName, child.Value)));

        var result = Match(left, right);

        Assert.Equal(
            [
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.interleaved.a"),
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.interleaved.b"),
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.interleaved.a"),
            ],
            Differences(result));
        Assert.Same(Child(left, "interleaved").Children[2], result.Differences[0].Left);
        Assert.Same(Child(left, "interleaved").Children[3], result.Differences[1].Left);
        Assert.Same(Child(left, "interleaved").Children[4], result.Differences[2].Left);
        Assert.All(result.Differences, difference => Assert.Null(difference.Right));

        // The left side's 43 nodes less the three surplus occurrences.
        Assert.Equal(40, result.MatchedCount);
    }

    // ---- Unicode normalization -----------------------------------------------------------------------------------

    [Fact]
    public void Nfc_and_nfd_forms_are_different_values_that_normalize_to_the_same_text()
    {
        var left = Child(Tree(Left.GetProperty("strings")), "hexCaseVariants").Children.Select(child => child.Value!).ToList();
        var right = Child(Tree(Right.GetProperty("strings")), "hexCaseVariants").Children.Select(child => child.Value!).ToList();

        // Three spellings of é on the left all decode to the one precomposed character; the third on the right is
        // e followed by a combining acute accent.
        Assert.Equal(["é", "é", "é"], left);
        Assert.Equal(["é", "é", "é"], right);
        Assert.NotEqual(left[2], right[2]);
        Assert.Equal(left[2], right[2].Normalize(NormalizationForm.FormC));

        var strings = MatchSection("strings");
        Assert.DoesNotContain("$.hexCaseVariants[*]", Differences(strings).Select(difference => difference.Path));
    }
}
