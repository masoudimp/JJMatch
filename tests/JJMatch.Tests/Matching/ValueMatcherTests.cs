using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace JJMatch.Tests.Matching;

// Every input is a section of TestData/ComplexJson, mostly the normalizedValues, numericValues and fuzzyValues
// sections, whose properties hold a pair of values on the two sides. A test may compare two different properties
// with each other to get a scenario the two sides do not hold. Exact, Normalized, Fuzzy and Numeric are
// implemented; Matches selects Normalized, MatchesExactly selects Exact, MatchesFuzzily selects Fuzzy and
// MatchesNumerically selects Numeric.
public class ValueMatcherTests
{
    private static readonly JsonElement Left = Load("Left.json");
    private static readonly JsonElement Right = Load("Right.json");

    private static JsonElement Load(string fileName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ComplexJson", fileName)),
            new JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.GetProperty("core").Clone();
    }

    // The fixture's bytes with the last byte of each marked text overwritten by 0xFF, which is invalid UTF-8, so
    // that a string value the parser accepts cannot be decoded. Same as in TraverserTests.
    private static JsonElement LoadWithInvalidUtf8(string fileName, params string[] markers)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "ComplexJson", fileName));
        foreach (var marker in markers)
        {
            var text = Encoding.UTF8.GetBytes(marker);
            var start = bytes.AsSpan().IndexOf(text);
            Assert.True(start >= 0 && start == bytes.AsSpan().LastIndexOf(text), $"'{marker}' must occur exactly once.");
            bytes[start + text.Length - 1] = 0xFF;
        }

        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.GetProperty("core").Clone();
    }

    private static JsonStructureNode Tree(JsonElement element) =>
        global::JJMatch.JsonStructureTraverser.Traverse(element);

    private static bool Matches(JsonStructureNode left, JsonStructureNode right) =>
        global::JJMatch.ValueMatcher.MatchValues(left, right, JJValueMatchingMode.Normalized);

    private static bool MatchesExactly(JsonStructureNode left, JsonStructureNode right) =>
        global::JJMatch.ValueMatcher.MatchValues(left, right, JJValueMatchingMode.Exact);

    private static bool MatchesNumerically(JsonStructureNode left, JsonStructureNode right) =>
        global::JJMatch.ValueMatcher.MatchValues(left, right, JJValueMatchingMode.Numeric);

    private static bool MatchesFuzzily(JsonStructureNode left, JsonStructureNode right) =>
        global::JJMatch.ValueMatcher.MatchValues(left, right, JJValueMatchingMode.Fuzzy);

    private static JsonStructureNode Child(JsonStructureNode node, string name) =>
        node.Children.Single(child => child.PropertyName == name);

    private static JsonStructureNode LeftValue(string name) =>
        Child(Tree(Left.GetProperty("normalizedValues")), name);

    private static JsonStructureNode RightValue(string name) =>
        Child(Tree(Right.GetProperty("normalizedValues")), name);

    // Whether the two sides of one property of the section match, under Normalized or under Exact.
    private static bool Section(string name) =>
        Matches(LeftValue(name), RightValue(name));

    private static bool ExactSection(string name) =>
        MatchesExactly(LeftValue(name), RightValue(name));

    private static JsonStructureNode LeftNumber(string name) =>
        Child(Tree(Left.GetProperty("numericValues")), name);

    private static JsonStructureNode RightNumber(string name) =>
        Child(Tree(Right.GetProperty("numericValues")), name);

    // Whether the two sides of one property of the numericValues section match under Numeric, after checking that
    // both are numbers written as the test expects.
    private static bool NumericSection(string name, string leftText, string rightText)
    {
        var left = LeftNumber(name);
        var right = RightNumber(name);
        Assert.Equal(JsonValueKind.Number, left.ValueKind);
        Assert.Equal(JsonValueKind.Number, right.ValueKind);
        Assert.Equal(leftText, left.Value);
        Assert.Equal(rightText, right.Value);
        return MatchesNumerically(left, right);
    }

    private static JsonStructureNode LeftFuzzy(string name) =>
        Child(Tree(Left.GetProperty("fuzzyValues")), name);

    private static JsonStructureNode RightFuzzy(string name) =>
        Child(Tree(Right.GetProperty("fuzzyValues")), name);

    // Whether the two sides of one property of the fuzzyValues section match under Fuzzy, after checking that both
    // are the strings the test expects.
    private static bool FuzzySection(string name, string leftText, string rightText)
    {
        var left = LeftFuzzy(name);
        var right = RightFuzzy(name);
        Assert.Equal(JsonValueKind.String, left.ValueKind);
        Assert.Equal(JsonValueKind.String, right.ValueKind);
        Assert.Equal(leftText, left.Value);
        Assert.Equal(rightText, right.Value);
        return MatchesFuzzily(left, right);
    }

    // Every primitive node of a tree, in document order.
    private static IEnumerable<JsonStructureNode> Leaves(JsonStructureNode node) =>
        node.Children.Count == 0 && node.ValueKind != JsonValueKind.Object && node.ValueKind != JsonValueKind.Array
            ? [node]
            : node.Children.SelectMany(Leaves);

    [Fact]
    public void Case_differences_are_ignored()
    {
        Assert.Equal("Masoud", LeftValue("casing").Value);
        Assert.Equal("masoud", RightValue("casing").Value);

        Assert.True(Section("casing"));
    }

    [Fact]
    public void Leading_and_trailing_whitespace_is_ignored()
    {
        Assert.Equal("  Hello  ", LeftValue("padding").Value);
        Assert.Equal("hello", RightValue("padding").Value);

        Assert.True(Section("padding"));
    }

    [Fact]
    public void Precomposed_and_decomposed_forms_are_equal()
    {
        var left = LeftValue("composed");
        var right = RightValue("composed");

        // é as one code point against e followed by a combining acute accent.
        Assert.Equal("café", left.Value);
        Assert.Equal("café", right.Value);
        Assert.NotEqual(left.Value, right.Value);

        Assert.True(Matches(left, right));
    }

    [Fact]
    public void Internal_whitespace_differences_are_not_ignored()
    {
        Assert.Equal("hello world", LeftValue("innerSpace").Value);
        Assert.Equal("helloworld", RightValue("innerSpace").Value);

        Assert.False(Section("innerSpace"));
    }

    [Fact]
    public void Punctuation_differences_are_not_ignored()
    {
        Assert.Equal("Hello!", LeftValue("punctuated").Value);
        Assert.Equal("hello", RightValue("punctuated").Value);

        Assert.False(Section("punctuated"));

        // "Hello!" against "  Hello  ": trimming does not remove the punctuation.
        Assert.False(Matches(LeftValue("punctuated"), LeftValue("padding")));
    }

    [Fact]
    public void Empty_and_whitespace_only_strings_are_equal_to_each_other()
    {
        Assert.Equal("", LeftValue("emptyText").Value);
        Assert.Equal("   ", LeftValue("blankText").Value);
        Assert.Equal("\t \n", RightValue("blankText").Value);

        Assert.True(Section("emptyText"));
        Assert.True(Section("blankText"));
        Assert.True(Matches(LeftValue("emptyText"), RightValue("blankText")));
        Assert.True(Matches(Tree(Left.GetProperty("comment")), LeftValue("blankText")));

        // An empty string matches nothing else.
        Assert.False(Matches(LeftValue("emptyText"), RightValue("casing")));
    }

    [Fact]
    public void Values_of_different_json_types_never_match()
    {
        // The text of a number, a Boolean and null against the value itself.
        Assert.Equal("123", LeftValue("numberText").Value);
        Assert.Equal(JsonValueKind.Number, RightValue("numberValue").ValueKind);
        Assert.False(Matches(LeftValue("numberText"), RightValue("numberValue")));

        Assert.Equal("true", LeftValue("booleanText").Value);
        Assert.Equal(JsonValueKind.True, RightValue("booleanValue").ValueKind);
        Assert.False(Matches(LeftValue("booleanText"), RightValue("booleanValue")));

        Assert.Equal("null", LeftValue("nullText").Value);
        Assert.Equal(JsonValueKind.Null, RightValue("nullValue").ValueKind);
        Assert.False(Matches(LeftValue("nullText"), RightValue("nullValue")));

        // Normalized is a rule for strings only: it says nothing about two numbers, Booleans or nulls, even equal
        // ones. Exact decides those.
        Assert.False(Section("numberValue"));
        Assert.False(Section("booleanValue"));
        Assert.False(Section("nullValue"));
    }

    [Fact]
    public void Comparison_is_culture_independent_including_turkish_casing()
    {
        var turkish = new CultureInfo("tr-TR");
        var left = LeftValue("turkish");

        // Under Turkish rules the capital of "i" is the dotted "İ", so a culture-sensitive comparison of "istanbul"
        // and "ISTANBUL" fails. The premise is checked so that the test means something.
        Assert.Equal("istanbul", left.Value);
        Assert.Equal("İSTANBUL", left.Value!.ToUpper(turkish));

        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { turkish, CultureInfo.InvariantCulture })
            {
                CultureInfo.CurrentCulture = culture;

                Assert.True(Section("turkish"));

                // The dotted capital İ (U+0130) and the dotless small ı (U+0131) are letters of their own in every
                // culture here: neither is the other case of i or I.
                Assert.Equal("İstanbul", LeftValue("dottedCapital").Value);
                Assert.Equal("istanbul", RightValue("dottedCapital").Value);
                Assert.False(Section("dottedCapital"));

                Assert.Equal("ıstanbul", LeftValue("dotlessSmall").Value);
                Assert.Equal("ISTANBUL", RightValue("dotlessSmall").Value);
                Assert.False(Section("dotlessSmall"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Lone_surrogates_are_compared_as_they_are_without_throwing()
    {
        var leftValues = Child(Tree(Left.GetProperty("loneSurrogates")), "values");
        var rightValues = Child(Tree(Right.GetProperty("loneSurrogates")), "values");

        // "\uD800", "\uDC00", "😀" and "x\uD800y" on both sides.
        Assert.Equal("\uD800", leftValues.Children[0].Value);
        Assert.Equal("\uDC00", leftValues.Children[1].Value);
        Assert.Equal("x\uD800y", leftValues.Children[3].Value);

        for (var index = 0; index < 4; index++)
        {
            Assert.True(Matches(leftValues.Children[index], rightValues.Children[index]));
        }
        Assert.False(Matches(leftValues.Children[0], rightValues.Children[1]));
        Assert.False(Matches(leftValues.Children[0], rightValues.Children[2]));
        Assert.False(Matches(leftValues.Children[0], rightValues.Children[3]));

        // A lone surrogate value nested under lone surrogate names.
        var nested = Child(Child(Tree(Left.GetProperty("loneSurrogates")), "nested"), "\uDC00").Children[0];
        Assert.True(Matches(Child(nested, "\uD800"), rightValues.Children[1]));

        // Trimming and case folding still apply around a lone surrogate.
        Assert.Equal(" X\uD800Y ", RightValue("lonePadded").Value);
        Assert.True(Section("lonePadded"));
        Assert.True(Matches(leftValues.Children[3], RightValue("lonePadded")));

        // Known limitation: a string holding a lone surrogate is not normalized, so NFC and NFD forms beside it stay
        // different.
        Assert.Equal("café\uD800", LeftValue("loneComposed").Value);
        Assert.Equal("café\uD800", RightValue("loneComposed").Value);
        Assert.False(Section("loneComposed"));
    }

    [Fact]
    public void Repeated_values_in_arrays_match_element_by_element()
    {
        var left = LeftValue("repeatedWords");
        var right = RightValue("repeatedWords");

        // ["Yes", "YES", " yes "] against ["yes", "yes", "yes"]: every element matches every other.
        Assert.Equal(["Yes", "YES", " yes "], left.Children.Select(element => element.Value));
        Assert.Equal(["yes", "yes", "yes"], right.Children.Select(element => element.Value));
        foreach (var leftElement in left.Children)
        {
            foreach (var rightElement in right.Children)
            {
                Assert.True(Matches(leftElement, rightElement));
            }
        }

        // ["ISO", "ANSI", "JIS"] against ["ISO", "ANSI"]: only the same words match.
        var leftOptions = Child(Tree(Left.GetProperty("items")[0].GetProperty("attributes")), "options");
        var rightOptions = Child(Tree(Right.GetProperty("items")[0].GetProperty("attributes")), "options");
        Assert.True(Matches(leftOptions.Children[0], rightOptions.Children[0]));
        Assert.True(Matches(leftOptions.Children[1], rightOptions.Children[1]));
        Assert.False(Matches(leftOptions.Children[0], rightOptions.Children[1]));
        Assert.False(Matches(leftOptions.Children[2], rightOptions.Children[0]));
    }

    [Fact]
    public void Nested_object_values_are_compared_like_any_other()
    {
        var left = Child(Child(LeftValue("deep"), "deeper"), "greeting");
        var right = Child(Child(RightValue("deep"), "deeper"), "greeting");

        Assert.Equal("Zoë Åkesson", left.Value);
        Assert.Equal(" ZOË ÅKESSON", right.Value);
        Assert.True(Matches(left, right));

        var leftCustomer = Tree(Left.GetProperty("customer"));
        var rightCustomer = Tree(Right.GetProperty("customer"));
        Assert.True(Matches(Child(leftCustomer, "name"), Child(rightCustomer, "name")));
        Assert.True(Matches(Child(leftCustomer, "name"), left));
        Assert.False(Matches(Child(leftCustomer, "e-mail"), Child(rightCustomer, "e-mail")));
    }

    [Theory]
    [InlineData(JJValueMatchingMode.Exact)]
    [InlineData(JJValueMatchingMode.Normalized)]
    [InlineData(JJValueMatchingMode.Numeric)]
    [InlineData(JJValueMatchingMode.Fuzzy)]
    [InlineData(JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized)]
    [InlineData(JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized | JJValueMatchingMode.Numeric)]
    [InlineData(JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric)]
    public void Result_is_deterministic_and_symmetric(JJValueMatchingMode mode)
    {
        var leaves = Leaves(Tree(Left)).Concat(Leaves(Tree(Right))).ToList();

        // Every primitive of both documents against every other, both ways round and twice over. Matches and
        // mismatches must both be present for the comparison to mean anything.
        var outcomes = new HashSet<bool>();
        foreach (var left in leaves)
        {
            foreach (var right in leaves)
            {
                var result = global::JJMatch.ValueMatcher.MatchValues(left, right, mode);

                outcomes.Add(result);
                Assert.Equal(result, global::JJMatch.ValueMatcher.MatchValues(right, left, mode));
                Assert.Equal(result, global::JJMatch.ValueMatcher.MatchValues(left, right, mode));
            }
        }
        Assert.Equal([false, true], outcomes.OrderBy(outcome => outcome));
    }

    [Fact]
    public void Original_values_are_left_unchanged()
    {
        var left = LeftValue("padding");
        var right = RightValue("composed");

        Assert.True(Matches(left, RightValue("padding")));
        Assert.True(Matches(LeftValue("composed"), right));

        // The nodes still hold the values exactly as the fixtures decode them.
        Assert.Equal(Left.GetProperty("normalizedValues").GetProperty("padding").GetString(), left.Value);
        Assert.Equal("  Hello  ", left.Value);
        Assert.Equal(Right.GetProperty("normalizedValues").GetProperty("composed").GetString(), right.Value);
        Assert.Equal("café", right.Value);

        // Normalization produces a new text and leaves the input alone; a text that needs no change is returned as is.
        var normalized = global::JJMatch.ValueMatcher.Normalize(left.Value!);
        Assert.Equal("Hello", normalized);
        Assert.Equal("  Hello  ", left.Value);
        Assert.Equal("café", global::JJMatch.ValueMatcher.Normalize(right.Value!));
        var unchanged = LeftValue("casing").Value!;
        Assert.Same(unchanged, global::JJMatch.ValueMatcher.Normalize(unchanged));
    }

    [Fact]
    public void Every_mode_is_a_distinct_power_of_two_and_none_is_zero()
    {
        var modes = Enum.GetValues(typeof(JJValueMatchingMode)).Cast<JJValueMatchingMode>().ToList();

        Assert.Equal(JJValueMatchingMode.None, default(JJValueMatchingMode));
        Assert.Equal(0, (int)JJValueMatchingMode.None);
        Assert.Equal(
            [
                JJValueMatchingMode.None,
                JJValueMatchingMode.Exact,
                JJValueMatchingMode.Normalized,
                JJValueMatchingMode.Fuzzy,
                JJValueMatchingMode.Numeric,
            ],
            modes);

        // Every mode but None is one bit, and no two share it, so any combination can be taken apart again.
        var flags = modes.Where(mode => mode != JJValueMatchingMode.None).Select(mode => (int)mode).ToList();
        Assert.All(flags, flag => Assert.True(flag > 0 && (flag & (flag - 1)) == 0, $"{flag} is not a power of two"));
        Assert.Equal(flags.Count, flags.Distinct().Count());
        Assert.Equal([1, 2, 4, 8], flags);
    }

    [Fact]
    public void Exact_and_normalized_are_separate_flags()
    {
        var both = JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized;

        Assert.True(both.HasFlag(JJValueMatchingMode.Exact));
        Assert.True(both.HasFlag(JJValueMatchingMode.Normalized));
        Assert.NotEqual(JJValueMatchingMode.Normalized, both);

        Assert.False(JJValueMatchingMode.Normalized.HasFlag(JJValueMatchingMode.Exact));
        Assert.False(JJValueMatchingMode.Exact.HasFlag(JJValueMatchingMode.Normalized));

        // None is in every mode only in the trivial sense of HasFlag; it holds no mode itself.
        Assert.Equal(JJValueMatchingMode.None, JJValueMatchingMode.None & both);
        Assert.All(
            Enum.GetValues(typeof(JJValueMatchingMode)).Cast<JJValueMatchingMode>().Where(mode => mode != JJValueMatchingMode.None),
            mode => Assert.False(JJValueMatchingMode.None.HasFlag(mode)));
    }

    [Fact]
    public void Normalized_applies_whenever_its_flag_is_set()
    {
        var left = LeftValue("casing");
        var right = RightValue("casing");

        // Modes combine with |: a pair matches when any enabled rule accepts it, so a rule for another kind of
        // value beside Normalized does not take it away.
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(left, right, JJValueMatchingMode.Normalized | JJValueMatchingMode.Numeric));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(left, right, JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(left, right, JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized));
    }

    [Fact]
    public void None_matches_nothing()
    {
        var left = LeftValue("casing");
        var right = RightValue("casing");
        var identical = Tree(Left.GetProperty("customer")).Children.Single(child => child.PropertyName == "name");

        // None holds no rule: it does not make even identical strings equal.
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(left, right, JJValueMatchingMode.None));
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(identical, identical, JJValueMatchingMode.None));
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(left, left, JJValueMatchingMode.None));

        // The former zero value of Exact must not be read as Normalized: a mode holding only Exact is Exact, which
        // accepts identical strings and rejects "Masoud" against "masoud", and 2 alone is Normalized.
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(left, right, (JJValueMatchingMode)1));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(left, left, (JJValueMatchingMode)1));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(identical, identical, (JJValueMatchingMode)1));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(left, right, (JJValueMatchingMode)2));
    }

    [Fact]
    public void Exact_strings_match_only_when_their_texts_are_identical()
    {
        // "Masoud" against "Masoud", and "123" against "123": the same text on both sides.
        Assert.Equal("Masoud", LeftValue("casing").Value);
        Assert.True(MatchesExactly(LeftValue("casing"), LeftValue("casing")));
        Assert.Equal("123", RightValue("numberText").Value);
        Assert.True(ExactSection("numberText"));
        Assert.Equal("Zoë Åkesson", Child(Tree(Right.GetProperty("customer")), "name").Value);
        Assert.True(MatchesExactly(Child(Tree(Left.GetProperty("customer")), "name"), Child(Tree(Right.GetProperty("customer")), "name")));

        // "Masoud" against "masoud", and "Yes" against "yes": a difference in case only is a difference.
        Assert.Equal("masoud", RightValue("casing").Value);
        Assert.False(ExactSection("casing"));
        Assert.Equal("Yes", LeftValue("repeatedWords").Children[0].Value);
        Assert.Equal("yes", RightValue("repeatedWords").Children[0].Value);
        Assert.False(MatchesExactly(LeftValue("repeatedWords").Children[0], RightValue("repeatedWords").Children[0]));

        // Comparison is ordinal, so the current culture plays no part and no case mapping, Turkish or other, applies.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { new CultureInfo("tr-TR"), CultureInfo.InvariantCulture })
            {
                CultureInfo.CurrentCulture = culture;
                Assert.False(ExactSection("turkish"));
                Assert.False(ExactSection("dottedCapital"));
                Assert.False(ExactSection("dotlessSmall"));
                Assert.True(ExactSection("numberText"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Exact_keeps_leading_and_trailing_whitespace()
    {
        // " yes " against "yes": a difference in padding only is a difference.
        var padded = LeftValue("repeatedWords").Children[2];
        var plain = RightValue("repeatedWords").Children[0];
        Assert.Equal(" yes ", padded.Value);
        Assert.Equal("yes", plain.Value);
        Assert.False(MatchesExactly(padded, plain));

        // "  Hello  " against "hello", and "Hello!" against "  Hello  ".
        Assert.False(ExactSection("padding"));
        Assert.False(MatchesExactly(LeftValue("punctuated"), LeftValue("padding")));

        // Two empty strings are identical; a white-space-only string equals neither an empty one nor a differently
        // blank one.
        Assert.True(ExactSection("emptyText"));
        Assert.True(MatchesExactly(Tree(Left.GetProperty("comment")), LeftValue("emptyText")));
        Assert.Equal("   ", LeftValue("blankText").Value);
        Assert.Equal("\t \n", RightValue("blankText").Value);
        Assert.False(ExactSection("blankText"));
        Assert.False(MatchesExactly(LeftValue("emptyText"), LeftValue("blankText")));

        // Nothing was trimmed on the nodes.
        Assert.Equal(" yes ", padded.Value);
        Assert.Equal("  Hello  ", LeftValue("padding").Value);
    }

    [Fact]
    public void Exact_compares_numbers_by_their_written_form()
    {
        // 123 against 123, and 30 against 30.
        Assert.Equal(JsonValueKind.Number, LeftValue("numberValue").ValueKind);
        Assert.Equal("123", LeftValue("numberValue").Value);
        Assert.True(ExactSection("numberValue"));
        var leftAge = Child(Tree(Left.GetProperty("duplicateKeys")), "age");
        var rightAge = Child(Tree(Right.GetProperty("duplicateKeys")), "age");
        Assert.Equal("30", leftAge.Value);
        Assert.True(MatchesExactly(leftAge, rightAge));

        // 1.0 against 1, 1.5E+10 against 15000000000: equal quantities written differently are not identical. The
        // two 30-digit integers differ in their last digit only, which a double cannot tell apart.
        var left = Tree(Left.GetProperty("primitiveValues")).Children;
        var right = Tree(Right.GetProperty("primitiveValues")).Children;
        Assert.Equal("1.0", left[2].Value);
        Assert.Equal("1", right[2].Value);
        Assert.False(MatchesExactly(left[2], right[2]));
        Assert.Equal("1.5E+10", left[1].Value);
        Assert.Equal("15000000000", right[1].Value);
        Assert.False(MatchesExactly(left[1], right[1]));
        Assert.Equal("123456789012345678901234567890", left[0].Value);
        Assert.Equal("123456789012345678901234567891", right[0].Value);
        Assert.False(MatchesExactly(left[0], right[0]));
        Assert.True(MatchesExactly(left[0], left[0]));

        // Different numbers, and a number against the text of itself.
        Assert.False(MatchesExactly(LeftValue("numberValue"), leftAge));
        Assert.False(MatchesExactly(LeftValue("numberValue"), RightValue("numberText")));
    }

    [Fact]
    public void Exact_booleans_and_null_match_only_themselves()
    {
        var leftFlags = Tree(Left.GetProperty("meta").GetProperty("flags"));
        var rightFlags = Tree(Right.GetProperty("meta").GetProperty("flags"));
        var leftValid = Child(Tree(Left.GetProperty("quotedNames").GetProperty("child")), "it's valid");
        var rightValid = Child(Tree(Right.GetProperty("quotedNames").GetProperty("child")), "it's valid");

        // true against true, false against false.
        Assert.Equal(JsonValueKind.True, LeftValue("booleanValue").ValueKind);
        Assert.True(ExactSection("booleanValue"));
        Assert.Equal(JsonValueKind.False, Child(leftFlags, "gift").ValueKind);
        Assert.True(MatchesExactly(Child(leftFlags, "gift"), Child(rightFlags, "gift")));

        // true against false, both ways.
        Assert.Equal(JsonValueKind.True, leftValid.ValueKind);
        Assert.Equal(JsonValueKind.False, rightValid.ValueKind);
        Assert.False(MatchesExactly(leftValid, rightValid));
        Assert.False(MatchesExactly(Child(leftFlags, "priority"), Child(leftFlags, "gift")));

        // null against null, and null against false, a number, a string and the text "null".
        Assert.Equal(JsonValueKind.Null, LeftValue("nullValue").ValueKind);
        Assert.True(ExactSection("nullValue"));
        Assert.True(MatchesExactly(LeftValue("nullValue"), Tree(Left.GetProperty("deletedAt"))));
        Assert.False(MatchesExactly(LeftValue("nullValue"), Child(rightFlags, "gift")));
        Assert.False(MatchesExactly(LeftValue("nullValue"), RightValue("numberValue")));
        Assert.False(MatchesExactly(LeftValue("nullValue"), RightValue("emptyText")));
        Assert.False(MatchesExactly(LeftValue("nullValue"), RightValue("nullText")));
    }

    [Fact]
    public void Exact_never_matches_values_of_different_json_types()
    {
        // "123" against 123, "true" against true, "null" against null: the text of a value is not the value.
        Assert.False(MatchesExactly(LeftValue("numberText"), RightValue("numberValue")));
        Assert.False(MatchesExactly(LeftValue("booleanText"), RightValue("booleanValue")));
        Assert.False(MatchesExactly(LeftValue("nullText"), RightValue("nullValue")));
        Assert.False(MatchesExactly(LeftValue("numberValue"), RightValue("booleanValue")));
        Assert.False(MatchesExactly(LeftValue("booleanValue"), RightValue("nullValue")));

        // Objects and arrays are not primitives and never match under Exact, even when identical.
        Assert.Equal(JsonValueKind.Object, LeftValue("deep").ValueKind);
        Assert.False(MatchesExactly(LeftValue("deep"), RightValue("deep")));
        Assert.False(MatchesExactly(LeftValue("deep"), LeftValue("deep")));
        Assert.Equal(JsonValueKind.Array, RightValue("repeatedWords").ValueKind);
        Assert.False(MatchesExactly(RightValue("repeatedWords"), RightValue("repeatedWords")));
        Assert.False(MatchesExactly(Tree(Left), Tree(Left)));
    }

    [Fact]
    public void Exact_does_not_normalize_unicode()
    {
        // "café" as one code point against "e" followed by a combining acute accent.
        var left = LeftValue("composed");
        var right = RightValue("composed");
        Assert.Equal("café", left.Value);
        Assert.Equal("café", right.Value);
        Assert.False(MatchesExactly(left, right));

        // The same beside a lone surrogate.
        Assert.False(ExactSection("loneComposed"));

        // Decoding is not normalization: the one string of primitiveValues is written with escapes in Left.json and
        // literally in Right.json, and the decoded texts are identical.
        var escaped = Tree(Left.GetProperty("primitiveValues")).Children[3];
        var literal = Tree(Right.GetProperty("primitiveValues")).Children[3];
        Assert.Equal("tab\there \"quoted\" back\\slash caf\u00e9 \U0001F680", escaped.Value);
        Assert.True(MatchesExactly(escaped, literal));
    }

    [Fact]
    public void Exact_lone_surrogates_match_only_when_identical_without_throwing()
    {
        var leftValues = Child(Tree(Left.GetProperty("loneSurrogates")), "values");
        var rightValues = Child(Tree(Right.GetProperty("loneSurrogates")), "values");

        // "\uD800", "\uDC00", "😀" and "x\uD800y" on both sides: each equals its twin and nothing else.
        Assert.Equal("\uD800", leftValues.Children[0].Value);
        Assert.Equal("\uDC00", leftValues.Children[1].Value);
        Assert.Equal("x\uD800y", leftValues.Children[3].Value);
        for (var index = 0; index < 4; index++)
        {
            for (var other = 0; other < 4; other++)
            {
                Assert.Equal(index == other, MatchesExactly(leftValues.Children[index], rightValues.Children[other]));
            }
        }

        // A lone surrogate value nested under lone surrogate names.
        var nested = Child(Child(Tree(Left.GetProperty("loneSurrogates")), "nested"), "\uDC00").Children[0];
        Assert.True(MatchesExactly(Child(nested, "\uD800"), rightValues.Children[1]));

        // Padding and case around a lone surrogate still count.
        Assert.Equal(" X\uD800Y ", RightValue("lonePadded").Value);
        Assert.True(MatchesExactly(leftValues.Children[3], LeftValue("lonePadded")));
        Assert.False(ExactSection("lonePadded"));
        Assert.False(MatchesExactly(leftValues.Children[3], RightValue("lonePadded")));
    }

    [Fact]
    public void Exact_undecodable_string_matches_nothing_and_is_not_null()
    {
        // The escapedValue of loneSurrogateNames is the same on both sides; in this copy of Left.json its bytes are
        // damaged, so the node is a String without a Value.
        var damaged = Child(Tree(LoadWithInvalidUtf8("Left.json", "\"escapedValue\": \"é").GetProperty("loneSurrogateNames")), "escapedValue");
        var intactLeft = Child(Tree(Left.GetProperty("loneSurrogateNames")), "escapedValue");
        var intactRight = Child(Tree(Right.GetProperty("loneSurrogateNames")), "escapedValue");
        Assert.Equal(JsonValueKind.String, damaged.ValueKind);
        Assert.Null(damaged.Value);
        Assert.NotNull(intactLeft.Value);
        Assert.Equal(intactLeft.Value, intactRight.Value);

        // The intact pair is identical; the damaged one equals nothing, itself and JSON null included, both ways
        // round and under every implemented mode.
        Assert.True(MatchesExactly(intactLeft, intactRight));
        foreach (var mode in new[] { JJValueMatchingMode.Exact, JJValueMatchingMode.Normalized, JJValueMatchingMode.Fuzzy, JJValueMatchingMode.Numeric, JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric })
        {
            Assert.False(global::JJMatch.ValueMatcher.MatchValues(damaged, damaged, mode));
            Assert.False(global::JJMatch.ValueMatcher.MatchValues(damaged, intactRight, mode));
            Assert.False(global::JJMatch.ValueMatcher.MatchValues(intactRight, damaged, mode));
            Assert.False(global::JJMatch.ValueMatcher.MatchValues(damaged, RightValue("nullValue"), mode));
            Assert.False(global::JJMatch.ValueMatcher.MatchValues(RightValue("nullValue"), damaged, mode));
            Assert.False(global::JJMatch.ValueMatcher.MatchValues(damaged, RightValue("emptyText"), mode));
        }
    }

    [Fact]
    public void Exact_compares_nested_values_and_array_elements_like_any_other()
    {
        // "Zoë Åkesson" deep in an object against the same text at the top of the customer, and against its padded
        // upper-case form on the other side.
        var leftGreeting = Child(Child(LeftValue("deep"), "deeper"), "greeting");
        var rightGreeting = Child(Child(RightValue("deep"), "deeper"), "greeting");
        Assert.Equal("Zoë Åkesson", leftGreeting.Value);
        Assert.Equal(" ZOË ÅKESSON", rightGreeting.Value);
        Assert.True(MatchesExactly(leftGreeting, Child(Tree(Left.GetProperty("customer")), "name")));
        Assert.False(MatchesExactly(leftGreeting, rightGreeting));
        Assert.True(Matches(leftGreeting, rightGreeting));

        // ["ISO", "ANSI", "JIS"] against ["ISO", "ANSI"]: only the same words match, element by element.
        var leftOptions = Child(Tree(Left.GetProperty("items")[0].GetProperty("attributes")), "options");
        var rightOptions = Child(Tree(Right.GetProperty("items")[0].GetProperty("attributes")), "options");
        Assert.Equal(["ISO", "ANSI", "JIS"], leftOptions.Children.Select(element => element.Value));
        Assert.Equal(["ISO", "ANSI"], rightOptions.Children.Select(element => element.Value));
        for (var index = 0; index < leftOptions.Children.Count; index++)
        {
            for (var other = 0; other < rightOptions.Children.Count; other++)
            {
                Assert.Equal(index == other, MatchesExactly(leftOptions.Children[index], rightOptions.Children[other]));
            }
        }

        // ["Yes", "YES", " yes "] against ["yes", "yes", "yes"]: no element is identical to any other, although
        // every one is equal under Normalized.
        foreach (var leftElement in LeftValue("repeatedWords").Children)
        {
            foreach (var rightElement in RightValue("repeatedWords").Children)
            {
                Assert.False(MatchesExactly(leftElement, rightElement));
                Assert.True(Matches(leftElement, rightElement));
            }
        }
    }

    [Fact]
    public void Exact_or_normalized_accepts_a_pair_that_either_rule_accepts()
    {
        var both = JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized;
        bool Both(JsonStructureNode left, JsonStructureNode right) => global::JJMatch.ValueMatcher.MatchValues(left, right, both);

        // Accepted by Normalized only: "Masoud" against "masoud", "café" in two Unicode forms.
        Assert.False(ExactSection("casing"));
        Assert.True(Section("casing"));
        Assert.True(Both(LeftValue("casing"), RightValue("casing")));
        Assert.True(Both(LeftValue("composed"), RightValue("composed")));

        // Accepted by Exact only: 123 against 123, true against true, null against null.
        foreach (var name in new[] { "numberValue", "booleanValue", "nullValue" })
        {
            Assert.True(ExactSection(name));
            Assert.False(Section(name));
            Assert.True(Both(LeftValue(name), RightValue(name)));
        }

        // Accepted by both: "123" against "123". Accepted by neither: "hello world" against "helloworld", and
        // "123" against 123.
        Assert.True(Both(LeftValue("numberText"), RightValue("numberText")));
        Assert.False(Both(LeftValue("innerSpace"), RightValue("innerSpace")));
        Assert.False(Both(LeftValue("numberText"), RightValue("numberValue")));

        // The order of the flags changes nothing.
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(LeftValue("numberValue"), RightValue("numberValue"), JJValueMatchingMode.Normalized | JJValueMatchingMode.Exact));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(LeftValue("casing"), RightValue("casing"), both | JJValueMatchingMode.Fuzzy));
    }

    [Fact]
    public void Numeric_compares_integers_and_decimals_by_their_quantity()
    {
        // 123 against 123.0, 1.5 against 1.50, 0.1 against 0.10: one quantity, written with or without a fraction
        // or trailing zeros. decimal agrees, as the independent reference.
        Assert.True(NumericSection("integerDecimal", "123", "123.0"));
        Assert.True(NumericSection("trailingZeros", "1.5", "1.50"));
        Assert.True(NumericSection("fraction", "0.1", "0.10"));
        foreach (var name in new[] { "integerDecimal", "trailingZeros", "fraction" })
        {
            Assert.Equal(
                decimal.Parse(LeftNumber(name).Value!, NumberStyles.Float, CultureInfo.InvariantCulture),
                decimal.Parse(RightNumber(name).Value!, NumberStyles.Float, CultureInfo.InvariantCulture));
            Assert.False(MatchesExactly(LeftNumber(name), RightNumber(name)));
        }

        // 1.0 against 1, and 123 against 123, equal as well; 123 against 30, 1.5 against 0.1 and 123 against 1.5 not.
        var left = Tree(Left.GetProperty("primitiveValues")).Children;
        var right = Tree(Right.GetProperty("primitiveValues")).Children;
        Assert.Equal("1.0", left[2].Value);
        Assert.Equal("1", right[2].Value);
        Assert.True(MatchesNumerically(left[2], right[2]));
        Assert.True(MatchesNumerically(LeftValue("numberValue"), RightValue("numberValue")));
        Assert.False(MatchesNumerically(LeftValue("numberValue"), Child(Tree(Left.GetProperty("duplicateKeys")), "age")));
        Assert.False(MatchesNumerically(LeftNumber("trailingZeros"), LeftNumber("fraction")));
        Assert.False(MatchesNumerically(LeftNumber("integerDecimal"), RightNumber("trailingZeros")));
    }

    [Fact]
    public void Numeric_reads_scientific_notation()
    {
        // 1e2 against 100, 1E+3 against 1000.000, 1.5E+10 against 15000000000: the exponent's sign, case and the
        // digits after the point all taken into account. decimal agrees.
        Assert.True(NumericSection("scientific", "1e2", "100"));
        Assert.True(NumericSection("exponentDecimal", "1E+3", "1000.000"));
        Assert.Equal(1000m, decimal.Parse(LeftNumber("exponentDecimal").Value!, NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.Equal(1000m, decimal.Parse(RightNumber("exponentDecimal").Value!, NumberStyles.Float, CultureInfo.InvariantCulture));
        var left = Tree(Left.GetProperty("primitiveValues")).Children;
        var right = Tree(Right.GetProperty("primitiveValues")).Children;
        Assert.Equal("1.5E+10", left[1].Value);
        Assert.Equal("15000000000", right[1].Value);
        Assert.True(MatchesNumerically(left[1], right[1]));
        Assert.False(MatchesExactly(left[1], right[1]));

        // 1e100 against 1e101, 1e2 against 1E+3: a different power is a different quantity.
        Assert.False(NumericSection("hugeExponent", "1e100", "1e101"));
        Assert.False(MatchesNumerically(LeftNumber("scientific"), LeftNumber("exponentDecimal")));
    }

    [Fact]
    public void Numeric_treats_positive_and_negative_zero_as_one()
    {
        // -0 against 0, 0.0 against -0.00e+5, and every zero against every other: zero has no sign and no scale.
        Assert.True(NumericSection("signedZero", "-0", "0"));
        Assert.True(NumericSection("zeroForms", "0.0", "-0.00e+5"));
        var zeros = new[]
        {
            LeftNumber("signedZero"), RightNumber("signedZero"), LeftNumber("zeroForms"), RightNumber("zeroForms"),
            RightNumber("tiny"), LeftNumber("amounts").Children[0].Children[2], Child(Tree(Left.GetProperty("items")[0]), "discount %"),
        };
        Assert.Equal(["-0", "0", "0.0", "-0.00e+5", "0", "-0.0", "0"], zeros.Select(zero => zero.Value));
        foreach (var left in zeros)
        {
            foreach (var right in zeros)
            {
                Assert.True(MatchesNumerically(left, right));
            }
        }

        // Exact keeps them apart, and no zero equals a nonzero quantity, however small or negative.
        Assert.False(MatchesExactly(LeftNumber("signedZero"), RightNumber("signedZero")));
        Assert.False(MatchesExactly(LeftNumber("zeroForms"), RightNumber("zeroForms")));
        Assert.False(MatchesNumerically(LeftNumber("signedZero"), LeftNumber("tiny")));
        Assert.False(MatchesNumerically(LeftNumber("signedZero"), LeftNumber("negative")));
    }

    [Fact]
    public void Numeric_keeps_very_large_integers_apart_that_a_double_cannot()
    {
        // 9007199254740992 against 9007199254740993: 2^53 and its successor. The premise, that a double cannot
        // tell them apart while BigInteger can, is checked first, so that the matcher is shown to do better.
        var left = LeftNumber("beyondDouble");
        var right = RightNumber("beyondDouble");
        Assert.Equal("9007199254740992", left.Value);
        Assert.Equal("9007199254740993", right.Value);
        Assert.Equal(double.Parse(left.Value!, CultureInfo.InvariantCulture), double.Parse(right.Value!, CultureInfo.InvariantCulture));
        Assert.Equal(BigInteger.Parse(left.Value!, CultureInfo.InvariantCulture) + 1, BigInteger.Parse(right.Value!, CultureInfo.InvariantCulture));
        Assert.False(MatchesNumerically(left, right));
        Assert.True(MatchesNumerically(left, left));
        Assert.True(MatchesNumerically(right, right));

        // The two 30-digit integers, differing in their last digit only.
        var leftLong = Tree(Left.GetProperty("primitiveValues")).Children[0];
        var rightLong = Tree(Right.GetProperty("primitiveValues")).Children[0];
        Assert.Equal("123456789012345678901234567890", leftLong.Value);
        Assert.Equal("123456789012345678901234567891", rightLong.Value);
        Assert.Equal(double.Parse(leftLong.Value!, CultureInfo.InvariantCulture), double.Parse(rightLong.Value!, CultureInfo.InvariantCulture));
        Assert.False(MatchesNumerically(leftLong, rightLong));
        Assert.True(MatchesNumerically(leftLong, leftLong));
    }

    [Fact]
    public void Numeric_keeps_a_very_small_nonzero_decimal_apart_from_zero()
    {
        // 1e-100 against 0. decimal rounds the small value to zero, so it would call them equal; the matcher does
        // not, because the quantity is nonzero.
        var left = LeftNumber("tiny");
        var right = RightNumber("tiny");
        Assert.Equal("1e-100", left.Value);
        Assert.Equal("0", right.Value);
        Assert.Equal(0m, decimal.Parse(left.Value!, NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.NotEqual(0d, double.Parse(left.Value!, CultureInfo.InvariantCulture));
        Assert.False(MatchesNumerically(left, right));
        Assert.True(MatchesNumerically(left, left));
    }

    [Fact]
    public void Numeric_handles_extreme_exponents_exactly()
    {
        // 1e99999999999999999999 against 0.1e100000000000000000000, and 1e-99999999999999999999 against
        // 10e-100000000000000000000: exponents beyond a long, equal only once the digits before and after the point
        // are taken into account. double overflows to infinity or underflows to zero on both sides, so it would
        // call the first pair equal for the wrong reason and could never tell 1e99999999999999999999 from
        // 2e99999999999999999999; the matcher reads the exponent exactly.
        Assert.True(NumericSection("extremeExponent", "1e99999999999999999999", "0.1e100000000000000000000"));
        Assert.True(NumericSection("extremeNegativeExponent", "1e-99999999999999999999", "10e-100000000000000000000"));
        Assert.True(BigInteger.Parse("99999999999999999999", CultureInfo.InvariantCulture) > long.MaxValue);
        Assert.True(double.IsPositiveInfinity(double.Parse(LeftNumber("extremeExponent").Value!, CultureInfo.InvariantCulture)));
        Assert.Equal(0d, double.Parse(LeftNumber("extremeNegativeExponent").Value!, CultureInfo.InvariantCulture));

        // A power off by one, or the opposite sign of the exponent, is a different quantity.
        Assert.False(MatchesNumerically(LeftNumber("extremeExponent"), LeftNumber("extremeNegativeExponent")));
        Assert.False(MatchesNumerically(LeftNumber("extremeExponent"), LeftNumber("hugeExponent")));
        Assert.False(MatchesNumerically(RightNumber("extremeNegativeExponent"), LeftNumber("tiny")));
    }

    [Fact]
    public void Numeric_compares_values_outside_the_range_of_double_and_decimal()
    {
        // 1e400 against 10E399: beyond double, which overflows to infinity on both sides.
        var leftDouble = LeftNumber("outsideDouble");
        var rightDouble = RightNumber("outsideDouble");
        Assert.Equal("1e400", leftDouble.Value);
        Assert.Equal("10E399", rightDouble.Value);
        Assert.True(double.IsPositiveInfinity(double.Parse(leftDouble.Value!, CultureInfo.InvariantCulture)));
        Assert.True(double.IsPositiveInfinity(double.Parse(rightDouble.Value!, CultureInfo.InvariantCulture)));
        Assert.True(MatchesNumerically(leftDouble, rightDouble));
        Assert.False(MatchesNumerically(leftDouble, LeftNumber("hugeExponent")));

        // 79228162514264337593543950336 against 7.9228162514264337593543950336E+28: decimal.MaxValue + 1, beyond
        // decimal, which rejects both. BigInteger agrees with the matcher.
        var leftDecimal = LeftNumber("outsideDecimal");
        var rightDecimal = RightNumber("outsideDecimal");
        Assert.Equal("79228162514264337593543950336", leftDecimal.Value);
        Assert.Equal("7.9228162514264337593543950336E+28", rightDecimal.Value);
        Assert.Throws<OverflowException>(() => decimal.Parse(leftDecimal.Value!, NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.Throws<OverflowException>(() => decimal.Parse(rightDecimal.Value!, NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.Equal(new BigInteger(decimal.MaxValue) + 1, BigInteger.Parse(leftDecimal.Value!, CultureInfo.InvariantCulture));
        Assert.True(MatchesNumerically(leftDecimal, rightDecimal));
        Assert.False(MatchesNumerically(leftDecimal, LeftNumber("beyondDouble")));
    }

    [Fact]
    public void Numeric_keeps_the_sign()
    {
        // -1.5e3 against -1500 equal; against 1500 and 1E+3 not.
        Assert.True(NumericSection("negative", "-1.5e3", "-1500"));
        Assert.Equal(-1500m, decimal.Parse(LeftNumber("negative").Value!, NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.False(MatchesNumerically(LeftNumber("negative"), LeftNumber("exponentDecimal")));
        Assert.False(MatchesNumerically(RightNumber("negative"), Tree(Right.GetProperty("primitiveValues")).Children[1]));
    }

    [Fact]
    public void Numeric_never_matches_values_of_other_json_types()
    {
        // "123" against 123, true against 1, null against 0: nothing is coerced into a number.
        Assert.Equal("123", LeftValue("numberText").Value);
        Assert.False(MatchesNumerically(LeftValue("numberText"), RightValue("numberValue")));
        Assert.False(MatchesNumerically(LeftValue("numberText"), LeftNumber("integerDecimal")));
        var one = Child(Tree(Left.GetProperty("items")[0]), "quantity");
        Assert.Equal("1", one.Value);
        Assert.Equal(JsonValueKind.True, RightValue("booleanValue").ValueKind);
        Assert.False(MatchesNumerically(RightValue("booleanValue"), one));
        var zero = Child(Tree(Left.GetProperty("items")[0]), "discount %");
        Assert.Equal("0", zero.Value);
        Assert.False(MatchesNumerically(LeftValue("nullValue"), zero));
        Assert.False(MatchesNumerically(LeftValue("nullValue"), RightNumber("signedZero")));

        // Numeric is a rule for two numbers only: identical strings, Booleans, nulls, objects and arrays are not
        // decided by it, and a number never equals a container.
        Assert.False(MatchesNumerically(LeftValue("numberText"), RightValue("numberText")));
        Assert.False(MatchesNumerically(LeftValue("booleanValue"), RightValue("booleanValue")));
        Assert.False(MatchesNumerically(LeftValue("nullValue"), RightValue("nullValue")));
        Assert.False(MatchesNumerically(LeftValue("deep"), RightValue("deep")));
        Assert.False(MatchesNumerically(LeftNumber("amounts"), RightNumber("amounts")));
        Assert.False(MatchesNumerically(LeftNumber("integerDecimal"), LeftNumber("amounts")));
    }

    [Fact]
    public void Numeric_compares_nested_values_and_array_elements_like_any_other()
    {
        // [1e2, 2.50, -0.0] against [100, 2.5, 0], nested in an object: each element equals its twin and nothing else.
        var left = Child(LeftNumber("amounts"), "values");
        var right = Child(RightNumber("amounts"), "values");
        Assert.Equal(["1e2", "2.50", "-0.0"], left.Children.Select(element => element.Value));
        Assert.Equal(["100", "2.5", "0"], right.Children.Select(element => element.Value));
        for (var index = 0; index < 3; index++)
        {
            for (var other = 0; other < 3; other++)
            {
                Assert.Equal(index == other, MatchesNumerically(left.Children[index], right.Children[other]));
                Assert.False(MatchesExactly(left.Children[index], right.Children[other]));
            }
        }

        // Elements against values elsewhere in the documents: 1e2 against 1e2 and 100 at the top of the section,
        // and 30 against 30 in duplicateKeys.
        Assert.True(MatchesNumerically(left.Children[0], LeftNumber("scientific")));
        Assert.True(MatchesNumerically(left.Children[0], RightNumber("scientific")));
        Assert.True(MatchesNumerically(Child(Tree(Left.GetProperty("duplicateKeys")), "age"), Child(Tree(Right.GetProperty("duplicateKeys")), "age")));
    }

    [Fact]
    public void Numeric_leaves_the_original_values_unchanged()
    {
        var left = LeftNumber("exponentDecimal");
        var right = RightNumber("exponentDecimal");
        var zero = LeftNumber("signedZero");

        Assert.True(MatchesNumerically(left, right));
        Assert.True(MatchesNumerically(zero, RightNumber("signedZero")));

        // The nodes still hold the numbers exactly as written in the fixtures, and Exact still tells them apart.
        Assert.Equal(Left.GetProperty("numericValues").GetProperty("exponentDecimal").GetRawText(), left.Value);
        Assert.Equal("1E+3", left.Value);
        Assert.Equal("1000.000", right.Value);
        Assert.Equal("-0", zero.Value);
        Assert.False(MatchesExactly(left, right));
        Assert.False(MatchesExactly(zero, RightNumber("signedZero")));
    }

    [Fact]
    public void Numeric_combines_with_exact_and_normalized_by_or()
    {
        var all = JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized | JJValueMatchingMode.Numeric;
        bool All(JsonStructureNode left, JsonStructureNode right) => global::JJMatch.ValueMatcher.MatchValues(left, right, all);

        // Accepted by Numeric only: 123 against 123.0. By Normalized only: "Masoud" against "masoud". By Exact only:
        // true against true, null against null.
        Assert.False(ExactSection("casing"));
        Assert.False(MatchesNumerically(LeftValue("casing"), RightValue("casing")));
        Assert.True(All(LeftValue("casing"), RightValue("casing")));
        Assert.False(MatchesExactly(LeftNumber("integerDecimal"), RightNumber("integerDecimal")));
        Assert.False(Matches(LeftNumber("integerDecimal"), RightNumber("integerDecimal")));
        Assert.True(All(LeftNumber("integerDecimal"), RightNumber("integerDecimal")));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(LeftNumber("integerDecimal"), RightNumber("integerDecimal"), JJValueMatchingMode.Exact | JJValueMatchingMode.Numeric));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(LeftNumber("integerDecimal"), RightNumber("integerDecimal"), JJValueMatchingMode.Normalized | JJValueMatchingMode.Numeric));
        Assert.True(All(LeftValue("booleanValue"), RightValue("booleanValue")));
        Assert.True(All(LeftValue("nullValue"), RightValue("nullValue")));

        // Accepted by neither: "123" against 123, 1e100 against 1e101, "hello world" against "helloworld".
        Assert.False(All(LeftValue("numberText"), RightValue("numberValue")));
        Assert.False(All(LeftNumber("hugeExponent"), RightNumber("hugeExponent")));
        Assert.False(All(LeftValue("innerSpace"), RightValue("innerSpace")));

        // Exact and Normalized are unchanged with Numeric beside them, and Fuzzy never decides two numbers.
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(LeftNumber("integerDecimal"), RightNumber("integerDecimal"), JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized));
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(LeftValue("casing"), RightValue("casing"), JJValueMatchingMode.Exact | JJValueMatchingMode.Numeric));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(LeftNumber("scientific"), RightNumber("scientific"), JJValueMatchingMode.Numeric | JJValueMatchingMode.Fuzzy));
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(LeftNumber("scientific"), RightNumber("scientific"), JJValueMatchingMode.Fuzzy));
    }

    [Fact]
    public void Numeric_agrees_with_integer_arithmetic_over_every_number_of_both_fixtures()
    {
        // Every number of both whole documents against every other, both ways round, checked against an
        // independent reference: the mantissa of each number as a BigInteger and its power of ten, made equal in
        // scale by actual multiplication. The matcher never multiplies, so the two agreeing on every pair is a real
        // check. The extreme values must be among the numbers for the check to mean anything.
        var leaves = Leaves(Tree(Left)).Concat(Leaves(Tree(Right))).ToList();
        var numbers = leaves.Where(leaf => leaf.ValueKind == JsonValueKind.Number).ToList();
        var others = leaves.Where(leaf => leaf.ValueKind != JsonValueKind.Number).ToList();
        Assert.True(numbers.Count >= 150, $"{numbers.Count} numbers");
        Assert.True(others.Count >= 100, $"{others.Count} other primitives");
        foreach (var text in new[] { "-0", "1e2", "1E+3", "1000.000", "9007199254740992", "9007199254740993", "1e-100", "1e400", "79228162514264337593543950336", "1e99999999999999999999", "10e-100000000000000000000", "-1.5e3" })
        {
            Assert.Contains(numbers, number => number.Value == text);
        }

        var parts = numbers.ToDictionary(number => number, number => MantissaAndPower(number.Value!));
        var maxDigits = parts.Values.Max(part => BigInteger.Abs(part.Mantissa).ToString(CultureInfo.InvariantCulture).Length);
        Assert.InRange(maxDigits, 29, 60);

        var matched = 0;
        var matchedButNotIdentical = 0;
        foreach (var left in numbers)
        {
            foreach (var right in numbers)
            {
                var expected = SameQuantity(parts[left], parts[right], maxDigits);
                var result = MatchesNumerically(left, right);
                Assert.True(expected == result, $"{left.Value} against {right.Value}: expected {expected}, got {result}");
                Assert.Equal(result, MatchesNumerically(right, left));

                // Identical texts are one quantity, and one quantity is accepted by Numeric whatever the texts.
                if (MatchesExactly(left, right))
                {
                    Assert.True(result, $"{left.Value} against {right.Value}: identical but not numerically equal");
                }
                else if (result)
                {
                    matchedButNotIdentical++;
                }
                if (result)
                {
                    matched++;
                }
            }

            // A number equals no string, Boolean or null, either way round.
            foreach (var other in others)
            {
                Assert.False(MatchesNumerically(left, other), $"{left.Value} against {other.ValueKind} {other.Value}");
                Assert.False(MatchesNumerically(other, left), $"{other.ValueKind} {other.Value} against {left.Value}");
            }
        }

        // Both outcomes occur, every number equals itself, and some pairs are equal by quantity only.
        Assert.True(matched >= numbers.Count, $"{matched} matches for {numbers.Count} numbers");
        Assert.True(matched < numbers.Count * numbers.Count, "every pair matched");
        Assert.True(matchedButNotIdentical >= 2 * 14, $"{matchedButNotIdentical} pairs equal by quantity only");
    }

    [Fact]
    public void Fuzzy_tolerates_one_edit_for_every_five_units_of_the_longer_text()
    {
        // One edit in ten units, one in five (exactly at the limit), one in nine.
        Assert.True(FuzzySection("typo", "Jon Smith", "John Smith"));
        Assert.True(FuzzySection("boundary", "Smith", "Smyth"));
        Assert.True(FuzzySection("digits", "SKU-10000", "SKU-10001"));

        // Two edits in fourteen units, and two in ten.
        Assert.True(FuzzySection("twoEdits", "Jonathon Smyth", "Jonathan Smith"));
        Assert.True(FuzzySection("swappedPairs", "Jhon Smiht", "John Smith"));

        // A pair that Normalized accepts has no edit at all.
        Assert.True(MatchesFuzzily(LeftValue("casing"), RightValue("casing")));
        Assert.True(MatchesFuzzily(LeftFuzzy("typo"), LeftFuzzy("typo")));
    }

    [Fact]
    public void Fuzzy_counts_a_transposition_as_one_edit()
    {
        // "recieve" against "receive": ie swapped to ei. As two substitutions the distance would be two, and two
        // edits in seven units would be too many.
        Assert.True(FuzzySection("transposed", "recieve", "receive"));

        // "Jhon Smiht" against "John Smith": two swaps in ten units, accepted as two edits, not four.
        Assert.True(FuzzySection("swappedPairs", "Jhon Smiht", "John Smith"));
    }

    [Fact]
    public void Fuzzy_rejects_a_pair_that_needs_more_edits()
    {
        // One edit in four units is one too many: the limit is one per five.
        Assert.False(FuzzySection("shortName", "Jon", "John"));

        // Four edits in twelve, five in six, and ten in ten: the words of "John Smith" in the other order are no
        // closer than a different name, because order is for Token.
        Assert.False(FuzzySection("manyEdits", "Johnny Smith", "Jon Smyth"));
        Assert.False(FuzzySection("unrelated", "apple", "orange"));
        Assert.False(FuzzySection("wordOrder", "John Smith", "Smith John"));

        // An accent is a different unit: "Hélène" against "Helene" is two edits in six.
        Assert.False(FuzzySection("accented", "Hélène", "Helene"));

        // Nothing is close to a text of a different length by more than the allowance.
        Assert.False(MatchesFuzzily(LeftValue("emptyText"), RightValue("casing")));
        Assert.False(MatchesFuzzily(LeftFuzzy("shortName"), RightFuzzy("typo")));
    }

    [Fact]
    public void Fuzzy_ignores_case_padding_and_unicode_form_before_counting_edits()
    {
        // "  jon smith " against "John Smith": once trimmed and case is ignored, one edit in ten units remains.
        Assert.True(FuzzySection("caseAndPadding", "  jon smith ", "John Smith"));

        // "Hélène" with combining accents against "Hélène" precomposed: no edit once both are in NFC, while as
        // UTF-16 units the texts differ by four in eight, which would be too many.
        Assert.Equal("Hélène", LeftFuzzy("composedForms").Value);
        Assert.Equal("Hélène", RightFuzzy("composedForms").Value);
        Assert.NotEqual(LeftFuzzy("composedForms").Value!.Length, RightFuzzy("composedForms").Value!.Length);
        Assert.True(FuzzySection("composedForms", "Hélène", "Hélène"));

        // What Normalized accepts, Fuzzy accepts, under a Turkish and the invariant culture alike.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { new CultureInfo("tr-TR"), CultureInfo.InvariantCulture })
            {
                CultureInfo.CurrentCulture = culture;
                foreach (var name in new[] { "casing", "padding", "composed", "turkish", "emptyText", "blankText" })
                {
                    Assert.True(Section(name), name);
                    Assert.True(MatchesFuzzily(LeftValue(name), RightValue(name)), name);
                }
                Assert.True(MatchesFuzzily(Child(Child(LeftValue("deep"), "deeper"), "greeting"), Child(Child(RightValue("deep"), "deeper"), "greeting")));
                Assert.True(FuzzySection("caseAndPadding", "  jon smith ", "John Smith"));
                Assert.False(FuzzySection("shortName", "Jon", "John"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Internal white space and punctuation are units like any other, so one of them is one edit.
        Assert.True(MatchesFuzzily(LeftValue("innerSpace"), RightValue("innerSpace")));
        Assert.True(MatchesFuzzily(LeftValue("punctuated"), RightValue("punctuated")));
    }

    [Fact]
    public void Fuzzy_accepts_every_pair_of_both_fixtures_that_normalized_accepts()
    {
        var strings = Leaves(Tree(Left)).Concat(Leaves(Tree(Right))).Where(leaf => leaf.ValueKind == JsonValueKind.String).ToList();
        var normalizedPairs = 0;
        foreach (var left in strings)
        {
            foreach (var right in strings)
            {
                if (Matches(left, right))
                {
                    normalizedPairs++;
                    Assert.True(MatchesFuzzily(left, right), $"'{left.Value}' against '{right.Value}'");
                }
            }
        }
        Assert.True(normalizedPairs > strings.Count, $"{normalizedPairs} pairs equal under Normalized");
    }

    [Fact]
    public void Fuzzy_never_matches_values_of_other_json_types()
    {
        // "123" against 123, true against true, null against null, 123 against 123.0: Fuzzy is a rule for two
        // strings only, so nothing is coerced and no other kind is decided by it, not even identical values.
        Assert.False(MatchesFuzzily(LeftValue("numberText"), RightValue("numberValue")));
        Assert.False(MatchesFuzzily(LeftValue("booleanText"), RightValue("booleanValue")));
        Assert.False(MatchesFuzzily(LeftValue("nullText"), RightValue("nullValue")));
        Assert.False(MatchesFuzzily(LeftValue("numberValue"), RightValue("numberValue")));
        Assert.False(MatchesFuzzily(LeftValue("booleanValue"), RightValue("booleanValue")));
        Assert.False(MatchesFuzzily(LeftValue("nullValue"), RightValue("nullValue")));
        Assert.False(MatchesFuzzily(LeftNumber("integerDecimal"), RightNumber("integerDecimal")));
        Assert.False(MatchesFuzzily(LeftNumber("scientific"), LeftNumber("scientific")));

        // Objects and arrays never match, whatever they hold.
        Assert.False(MatchesFuzzily(LeftValue("deep"), RightValue("deep")));
        Assert.False(MatchesFuzzily(LeftFuzzy("people"), RightFuzzy("people")));
        Assert.False(MatchesFuzzily(Child(LeftFuzzy("people"), "names"), Child(RightFuzzy("people"), "names")));
        Assert.False(MatchesFuzzily(LeftFuzzy("typo"), LeftFuzzy("people")));
    }

    [Fact]
    public void Fuzzy_compares_lone_surrogates_as_they_are_without_throwing()
    {
        // "x\uD800y" against " X\uD800Y ": trimmed and case ignored, no edit remains, and the lone surrogate is a
        // unit like any other.
        Assert.Equal("x\uD800y", LeftValue("lonePadded").Value);
        Assert.True(MatchesFuzzily(LeftValue("lonePadded"), RightValue("lonePadded")));
        Assert.True(MatchesFuzzily(LeftValue("lonePadded"), LeftValue("lonePadded")));

        // Different lone surrogates differ by one unit, which is one edit in three: too many.
        var leftValues = Child(Tree(Left.GetProperty("loneSurrogates")), "values");
        Assert.True(leftValues.Children.Count >= 2);
        Assert.NotEqual(leftValues.Children[0].Value, leftValues.Children[1].Value);
        Assert.True(MatchesFuzzily(leftValues.Children[0], leftValues.Children[0]));
        Assert.False(MatchesFuzzily(leftValues.Children[0], leftValues.Children[1]));

        // The NFC limitation holds under Fuzzy too: beside a lone surrogate the decomposed "café" is not composed,
        // so it is two units longer, two edits in six.
        Assert.Equal("café\uD800", LeftValue("loneComposed").Value);
        Assert.Equal("café\uD800", RightValue("loneComposed").Value);
        Assert.False(Section("loneComposed"));
        Assert.False(MatchesFuzzily(LeftValue("loneComposed"), RightValue("loneComposed")));
    }

    [Fact]
    public void Fuzzy_compares_nested_values_and_array_elements_like_any_other()
    {
        // ["Jon Smith", "Jane Doe"] against ["John Smith", "Jane Do"], nested in an object: each element is one edit
        // from its twin and far from the other.
        var left = Child(LeftFuzzy("people"), "names");
        var right = Child(RightFuzzy("people"), "names");
        Assert.Equal(["Jon Smith", "Jane Doe"], left.Children.Select(element => element.Value));
        Assert.Equal(["John Smith", "Jane Do"], right.Children.Select(element => element.Value));
        for (var index = 0; index < 2; index++)
        {
            for (var other = 0; other < 2; other++)
            {
                Assert.Equal(index == other, MatchesFuzzily(left.Children[index], right.Children[other]));
                Assert.False(MatchesExactly(left.Children[index], right.Children[other]));
            }
        }

        // Elements against values elsewhere in the documents.
        Assert.True(MatchesFuzzily(left.Children[0], RightFuzzy("typo")));
        Assert.True(MatchesFuzzily(right.Children[0], LeftFuzzy("caseAndPadding")));
        Assert.False(MatchesFuzzily(left.Children[0], RightFuzzy("wordOrder")));
    }

    [Fact]
    public void Fuzzy_leaves_the_original_values_unchanged()
    {
        var left = LeftFuzzy("caseAndPadding");
        var right = RightFuzzy("composedForms");

        Assert.True(MatchesFuzzily(left, RightFuzzy("caseAndPadding")));
        Assert.True(MatchesFuzzily(LeftFuzzy("composedForms"), right));

        // The nodes still hold the values exactly as the fixtures decode them, and Exact still tells them apart.
        Assert.Equal(Left.GetProperty("fuzzyValues").GetProperty("caseAndPadding").GetString(), left.Value);
        Assert.Equal("  jon smith ", left.Value);
        Assert.Equal("Hélène", right.Value);
        Assert.Equal("Hélène", LeftFuzzy("composedForms").Value);
        Assert.False(MatchesExactly(left, RightFuzzy("caseAndPadding")));
        Assert.False(MatchesExactly(LeftFuzzy("composedForms"), right));
    }

    [Fact]
    public void Fuzzy_combines_with_the_other_modes_by_or()
    {
        var all = JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric;
        bool All(JsonStructureNode left, JsonStructureNode right) => global::JJMatch.ValueMatcher.MatchValues(left, right, all);

        // Accepted by Fuzzy only: "Jon Smith" against "John Smith". By Numeric only: 123 against 123.0. By Exact
        // only: true against true.
        Assert.False(MatchesExactly(LeftFuzzy("typo"), RightFuzzy("typo")));
        Assert.False(Matches(LeftFuzzy("typo"), RightFuzzy("typo")));
        Assert.False(MatchesNumerically(LeftFuzzy("typo"), RightFuzzy("typo")));
        Assert.True(All(LeftFuzzy("typo"), RightFuzzy("typo")));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(LeftFuzzy("typo"), RightFuzzy("typo"), JJValueMatchingMode.Exact | JJValueMatchingMode.Fuzzy));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(LeftFuzzy("typo"), RightFuzzy("typo"), JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric));
        Assert.True(All(LeftNumber("integerDecimal"), RightNumber("integerDecimal")));
        Assert.True(All(LeftValue("booleanValue"), RightValue("booleanValue")));

        // Accepted by neither: "Jon" against "John", "123" against 123, 1e100 against 1e101.
        Assert.False(All(LeftFuzzy("shortName"), RightFuzzy("shortName")));
        Assert.False(All(LeftValue("numberText"), RightValue("numberValue")));
        Assert.False(All(LeftNumber("hugeExponent"), RightNumber("hugeExponent")));

        // The other rules are unchanged with Fuzzy beside them, and Numeric never decides two strings.
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(LeftFuzzy("typo"), RightFuzzy("typo"), JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized | JJValueMatchingMode.Numeric));
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(LeftNumber("integerDecimal"), RightNumber("integerDecimal"), JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Normalized));
        Assert.True(global::JJMatch.ValueMatcher.MatchValues(LeftFuzzy("typo"), RightFuzzy("typo"), JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric));
        Assert.False(global::JJMatch.ValueMatcher.MatchValues(LeftFuzzy("typo"), RightFuzzy("typo"), JJValueMatchingMode.Numeric));
    }

    [Fact]
    public void Fuzzy_agrees_with_a_reference_distance_over_every_string_of_both_fixtures()
    {
        // Every string of both whole documents against every other, both ways round, checked against an
        // independent reference: the full optimal string alignment table over the trimmed, composed, upper-cased
        // texts, accepted when five times the distance is at most the longer length. The matcher keeps three rows
        // and stops early, so the two agreeing on every pair is a real check.
        var leaves = Leaves(Tree(Left)).Concat(Leaves(Tree(Right))).ToList();
        var strings = leaves.Where(leaf => leaf.ValueKind == JsonValueKind.String).ToList();
        var others = leaves.Where(leaf => leaf.ValueKind != JsonValueKind.String).ToList();
        Assert.True(strings.Count >= 100, $"{strings.Count} strings");
        Assert.True(others.Count >= 150, $"{others.Count} other primitives");
        foreach (var text in new[] { "Jon Smith", "recieve", "  jon smith ", "Hélène", "Smith John", "x\uD800y", "" })
        {
            Assert.Contains(strings, value => value.Value == text);
        }

        var folded = strings.ToDictionary(value => value, value => ReferenceNormalize(value.Value!).ToUpperInvariant());
        var matched = 0;
        var matchedButNotNormalized = 0;
        foreach (var left in strings)
        {
            foreach (var right in strings)
            {
                var distance = ReferenceDistance(folded[left], folded[right]);
                var expected = 5 * distance <= Math.Max(folded[left].Length, folded[right].Length);
                var result = MatchesFuzzily(left, right);
                Assert.True(expected == result, $"'{left.Value}' against '{right.Value}': distance {distance}, expected {expected}, got {result}");
                Assert.Equal(result, MatchesFuzzily(right, left));

                if (Matches(left, right))
                {
                    Assert.True(result, $"'{left.Value}' against '{right.Value}': equal under Normalized but not Fuzzy");
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

            // A string equals no number, Boolean or null, either way round.
            foreach (var other in others)
            {
                Assert.False(MatchesFuzzily(left, other), $"'{left.Value}' against {other.ValueKind} {other.Value}");
                Assert.False(MatchesFuzzily(other, left), $"{other.ValueKind} {other.Value} against '{left.Value}'");
            }
        }

        // Both outcomes occur, every string equals itself, and some pairs are equal by distance only.
        Assert.True(matched >= strings.Count, $"{matched} matches for {strings.Count} strings");
        Assert.True(matched < strings.Count * strings.Count, "every pair matched");
        Assert.True(matchedButNotNormalized >= 2 * 10, $"{matchedButNotNormalized} pairs equal by distance only");
    }

    [Fact]
    public void Fuzzy_similarity_is_not_transitive()
    {
        var first = LeftFuzzy("swappedPairs");
        var middle = LeftFuzzy("typo");
        var last = RightFuzzy("manyEdits");
        Assert.Equal("Jhon Smiht", first.Value);
        Assert.Equal("Jon Smith", middle.Value);
        Assert.Equal("Jon Smyth", last.Value);

        // Distances after folding are 2, 1 and 3; all longer lengths are 9 or 10, so allowances are 1 or 2.
        // Fuzzy cannot be used as a hash-set equality or as a transitive equivalence for collapsing array shapes.
        Assert.True(MatchesFuzzily(first, middle));
        Assert.True(MatchesFuzzily(middle, last));
        Assert.False(MatchesFuzzily(first, last));
    }

    [Fact]
    public void Normalization_agrees_with_the_runtime_and_is_idempotent_for_fixture_strings()
    {
        foreach (var leaf in Leaves(Tree(Left)).Concat(Leaves(Tree(Right))))
        {
            if (leaf.ValueKind != JsonValueKind.String || leaf.Value is null) continue;
            var actual = global::JJMatch.ValueMatcher.Normalize(leaf.Value);
            Assert.Equal(ReferenceNormalize(leaf.Value), actual);
            Assert.Equal(actual, global::JJMatch.ValueMatcher.Normalize(actual));
        }
    }

    // Independent of the production preflight scan: ask the runtime to normalize, and preserve the trimmed text
    // if it rejects malformed UTF-16 or noncharacters. Used by both the normalization and fuzzy-distance oracles.
    private static string ReferenceNormalize(string text)
    {
        var trimmed = text.Trim();
        try
        {
            return trimmed.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
    }

    // The optimal string alignment distance by the textbook table, every cell kept and no bound applied.
    private static int ReferenceDistance(string left, string right)
    {
        var table = new int[left.Length + 1, right.Length + 1];
        for (var row = 0; row <= left.Length; row++)
        {
            table[row, 0] = row;
        }
        for (var column = 0; column <= right.Length; column++)
        {
            table[0, column] = column;
        }
        for (var row = 1; row <= left.Length; row++)
        {
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                table[row, column] = Math.Min(Math.Min(table[row - 1, column] + 1, table[row, column - 1] + 1), table[row - 1, column - 1] + cost);
                if (row > 1 && column > 1 && left[row - 1] == right[column - 2] && left[row - 2] == right[column - 1])
                {
                    table[row, column] = Math.Min(table[row, column], table[row - 2, column - 2] + 1);
                }
            }
        }
        return table[left.Length, right.Length];
    }

    // A JSON number as an integer mantissa (all its digits, signed) and the power of ten it is scaled by: the
    // written exponent minus the number of digits after the point. Nothing is stripped or canonicalized here.
    private static (BigInteger Mantissa, BigInteger Power) MantissaAndPower(string raw)
    {
        var exponentAt = raw.IndexOfAny(['e', 'E']);
        var mantissaText = exponentAt < 0 ? raw : raw.Substring(0, exponentAt);
        var pointAt = mantissaText.IndexOf('.');
        var fractionLength = pointAt < 0 ? 0 : mantissaText.Length - pointAt - 1;
        var mantissa = BigInteger.Parse(mantissaText.Replace(".", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var power = exponentAt < 0 ? BigInteger.Zero : BigInteger.Parse(raw.Substring(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return (mantissa, power - fractionLength);
    }

    // Whether m1 × 10^p1 equals m2 × 10^p2, by bringing both to the smaller power and comparing the products.
    // Two nonzero numbers whose powers differ by more than the digit count of either mantissa cannot be equal,
    // because |m| < 10^maxDigits; that bound keeps the power that is computed small, so the extreme exponents
    // of the fixtures are decided without raising ten to them.
    private static bool SameQuantity((BigInteger Mantissa, BigInteger Power) left, (BigInteger Mantissa, BigInteger Power) right, int maxDigits)
    {
        if (left.Mantissa.IsZero || right.Mantissa.IsZero)
        {
            return left.Mantissa.IsZero && right.Mantissa.IsZero;
        }
        var shift = left.Power - right.Power;
        if (BigInteger.Abs(shift) > maxDigits)
        {
            return false;
        }
        return shift >= 0
            ? left.Mantissa * BigInteger.Pow(10, (int)shift) == right.Mantissa
            : left.Mantissa == right.Mantissa * BigInteger.Pow(10, (int)-shift);
    }
}
