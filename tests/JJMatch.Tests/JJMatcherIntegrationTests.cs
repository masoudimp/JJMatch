namespace JJMatch.Tests;

public class JJMatcherIntegrationTests
{
    // Guide.md uses this exact pair in every row. Counts below are hand-tallied from that document,
    // independent of the production tree, shape signatures and assignment engine. customer.name
    // ("  Alice Chen  " against "alice chen") folds to equal under Normalized, so whenever the configured value
    // mode does not already accept it, it is one lighter ValueCaseMismatch instead of a full ValueMismatch.
    [Theory]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.None, JJMatchOptions.All, 14, 20, 4, 1)]
    [InlineData(JJArrayMatchingMode.StructureOnly, JJValueMatchingMode.None, JJMatchOptions.All, 11, 17, 4, 1)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Numeric, JJMatchOptions.All, 15, 20, 3, 1)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Normalized, JJMatchOptions.All, 15, 20, 4, 0)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Fuzzy, JJMatchOptions.All, 16, 20, 3, 0)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Numeric | JJValueMatchingMode.Normalized, JJMatchOptions.All, 16, 20, 3, 0)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.None, JJMatchOptions.StructuralMatching, 19, 20, 0, 0)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Numeric | JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy, JJMatchOptions.All, 17, 20, 2, 0)]
    public void Guide_configuration_table_matches_independently_counted_nodes(
        JJArrayMatchingMode arrayMode, JJValueMatchingMode valueMode, JJMatchOptions options,
        int matchedNodes, int effectiveNodesPerSide, int valueDifferences, int valueCaseDifferences)
    {
        var left = Input("Left", "guideScenario");
        var right = Input("Right", "guideScenario");
        var settings = new JJMatchSettings { ArrayMode = arrayMode, ValueMode = valueMode };
        var result = JJMatcher.Match(left, right, options, settings);
        Consistent(result);
        Assert.False(result.IsMatch);
        Assert.Equal(200d * matchedNodes / (2 * effectiveNodesPerSide), result.Score);
        Assert.Equal(valueDifferences + valueCaseDifferences + 2, result.Differences.Count);
        Assert.Equal(valueDifferences, result.Differences.Count(d => d.Kind == JJDifferenceKind.ValueMismatch));
        Assert.Equal(valueCaseDifferences, result.Differences.Count(d => d.Kind == JJDifferenceKind.ValueCaseMismatch));
        Assert.Equal("$.legacyCode", Assert.Single(result.Differences, d => d.Kind == JJDifferenceKind.Missing).Path);
        Assert.Equal("$.coupon", Assert.Single(result.Differences, d => d.Kind == JJDifferenceKind.Added).Path);
        Assert.Equal(result.Score, JJMatcher.Match(right, left, options, settings).Score);
    }

    [Theory]
    [InlineData("total", JJArrayMatchingMode.Strict, JJValueMatchingMode.Numeric, JJMatchOptions.All, 0, 1)]
    [InlineData("customer.name", JJArrayMatchingMode.Strict, JJValueMatchingMode.Normalized, JJMatchOptions.All, 0, 1)]
    [InlineData("customer.contact", JJArrayMatchingMode.Strict, JJValueMatchingMode.Fuzzy, JJMatchOptions.All, 0, 1)]
    [InlineData("customer", JJArrayMatchingMode.Strict, JJValueMatchingMode.Fuzzy, JJMatchOptions.All, 50, 2)]
    [InlineData("paid", JJArrayMatchingMode.Strict, JJValueMatchingMode.None, JJMatchOptions.StructuralMatching, 0, 1)]
    public void Guide_focused_examples_switch_from_mismatch_to_complete_match(
        string propertyPath, JJArrayMatchingMode arrayMode, JJValueMatchingMode valueMode, JJMatchOptions options,
        double defaultScore, int defaultDifferences)
    {
        var left = Fixture("Left").GetProperty("publicApi").GetProperty("guideScenario");
        var right = Fixture("Right").GetProperty("publicApi").GetProperty("guideScenario");
        foreach (var name in propertyPath.Split('.'))
        {
            left = left.GetProperty(name);
            right = right.GetProperty(name);
        }
        var before = JJMatcher.Match(left.GetRawText(), right.GetRawText());
        Consistent(before);
        Assert.False(before.IsMatch);
        Assert.Equal(defaultScore, before.Score);
        Assert.Equal(defaultDifferences, before.Differences.Count);
        // customer.name folds to equal under Normalized ("  Alice Chen  " against "alice chen"), so it is the
        // lighter ValueCaseMismatch; every other difference here is a full ValueMismatch.
        Assert.All(before.Differences, d => Assert.Contains(d.Kind, new[] { JJDifferenceKind.ValueMismatch, JJDifferenceKind.ValueCaseMismatch }));
        var after = JJMatcher.Match(left.GetRawText(), right.GetRawText(), options,
            new JJMatchSettings { ArrayMode = arrayMode, ValueMode = valueMode });
        Consistent(after);
        Assert.True(after.IsMatch);
        Assert.Equal(100, after.Score);
        Assert.Empty(after.Differences);
    }

    private static readonly string TestDataDirectory =
        Path.Combine(AppContext.BaseDirectory, "TestData", "ComplexJson");

    [Theory]
    [InlineData("Left")]
    [InlineData("Right")]
    public void Stringified_fixture_decodes_to_the_exact_json_fixture_text(string side)
    {
        var json = File.ReadAllText(Path.Combine(TestDataDirectory, side + ".json"));
        var stringified = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Stringified", side + "Stringify.txt"));
        using var document = System.Text.Json.JsonDocument.Parse(stringified);
        Assert.Equal(System.Text.Json.JsonValueKind.String, document.RootElement.ValueKind);
        // Text equality also preserves duplicate names, numeric spellings and lone-surrogate escape sequences.
        Assert.Equal(json, document.RootElement.GetString());
    }

    [Fact]
    public void Complex_documents_are_scored_structurally()
    {
        var left = Fixture("Left").GetProperty("core").GetRawText();
        var right = Fixture("Right").GetProperty("core").GetRawText();
        var result = JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching,
            new JJMatchSettings { ArrayMode = JJArrayMatchingMode.StructureOnly });
        Consistent(result);
        Assert.False(result.IsMatch);
        Assert.InRange(result.Score, double.Epsilon, 99.999);
        Assert.NotEmpty(result.Differences);
        Assert.All(result.Differences, difference => Assert.NotEqual(JJDifferenceKind.ValueMismatch, difference.Kind));
    }

    // Keep enum numeric compatibility while choosing an explicit, intuitive public default.
    [Fact]
    public void Array_matching_mode_defaults_to_structure_only_and_strict_is_the_other_mode()
    {
        Assert.Equal(JJArrayMatchingMode.StructureOnly, default(JJArrayMatchingMode));
        Assert.Equal(JJArrayMatchingMode.Strict, new JJMatchSettings().ArrayMode);

        Assert.Equal(
            [JJArrayMatchingMode.StructureOnly, JJArrayMatchingMode.Strict],
            Enum.GetValues<JJArrayMatchingMode>());

        Assert.Equal(1, (int)JJArrayMatchingMode.Strict);
    }
    [Fact]
    public void Public_paths_escape_names_and_preserve_duplicate_occurrences()
    {
        var left = Fixture("Left").GetProperty("core");
        var right = Fixture("Right").GetProperty("core");
        var escaped = JJMatcher.Match(left.GetProperty("pathEscapes").GetRawText(), right.GetProperty("pathEscapes").GetRawText());
        Consistent(escaped);
        Assert.Contains(escaped.Differences, d => d.Path == "$['line\\u2028sep']['2nd']" && d.Kind == JJDifferenceKind.Added);
        Assert.Contains(escaped.Differences, d => d.Path == "$._['del\\u007F']" && d.Kind == JJDifferenceKind.TypeMismatch);
        Assert.All(escaped.Differences, d => Assert.DoesNotContain('\n', d.Path));

        var quoted = JJMatcher.Match(left.GetProperty("quotedNames").GetRawText(), right.GetProperty("quotedNames").GetRawText());
        Assert.Equal("$['owner\\'s name']", quoted.Differences[0].Path);
        Assert.Equal("Ali", quoted.Differences[0].LeftValue!.Value.GetString());

        // Removing the entire object reports its original occurrences, including malformed escaped names.
        var missing = JJMatcher.Match(left.GetProperty("loneSurrogates").GetRawText(), Input("Right", "emptyObject"));
        Assert.Equal("$['\\uD800']#1", missing.Differences[0].Path);
        Assert.Equal("$['\\uD800']#2", missing.Differences[3].Path);
        Assert.Equal("high again", missing.Differences[3].LeftValue!.Value.GetString());
        Assert.True(JJMatcher.Match(left.GetProperty("loneSurrogates").GetRawText(), right.GetProperty("loneSurrogates").GetRawText()).IsMatch);
    }

    [Fact]
    public async Task Concurrent_calls_and_failed_calls_do_not_share_state()
    {
        var left = Input("Left", "shapeValues");
        var right = Input("Right", "shapeValues");
        var settings = new JJMatchSettings { ArrayMode = JJArrayMatchingMode.StructureOnly };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => JJMatcher.Match(left, right, cancellationToken: cancellation.Token));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(left, right, settings: new JJMatchSettings { MaxWork = 1 }));
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(() =>
            JJMatcher.Match(left, right, settings: index % 2 == 0 ? settings : null))));
        for (var index = 0; index < results.Length; index++)
        {
            Consistent(results[index]);
            Assert.Equal(index % 2 == 0 ? 100 : 50, results[index].Score);
        }
        Assert.Equal(JJArrayMatchingMode.StructureOnly, settings.ArrayMode);
    }

    private static System.Text.Json.JsonElement Fixture(string side)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(TestDataDirectory, side + ".json")),
            new System.Text.Json.JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.Clone();
    }

    private static string Input(string side, string name) => Fixture(side).GetProperty("publicApi").GetProperty(name).GetRawText();

    private static string Core(string side, string section, string name) =>
        Fixture(side).GetProperty("core").GetProperty(section).GetProperty(name).GetRawText();

    private static void Consistent(JJMatchResult result)
    {
        Assert.InRange(result.Score, 0, 100);
        Assert.Equal(result.Differences.Count == 0, result.IsMatch);
        Assert.Equal(result.Score == 100, result.IsMatch);
        Assert.True(result.ElapsedMilliseconds >= 0);
    }

    [Fact]
    public void Default_call_returns_explainable_score_and_detached_values()
    {
        var result = JJMatcher.Match(Input("Left", "basic"), Input("Right", "basic"));
        // Object and name match; age differs: M=2, L=R=3, Dice=4/6.
        Consistent(result);
        Assert.False(result.IsMatch);
        Assert.Equal(200d / 3, result.Score);
        Assert.Equal(100d, result.StructuralMatchPercentage);
        Assert.Equal(result.Score, result.ValueMatchPercentage);
        var difference = Assert.Single(result.Differences);
        Assert.Equal(JJDifferenceKind.ValueMismatch, difference.Kind);
        Assert.Equal(10, difference.ScoreWeight); // Under Standard every difference counts fully.
        Assert.Equal("$.age", difference.Path);
        Assert.Equal("$.age", difference.LeftPath);
        Assert.Equal("$.age", difference.RightPath);
        // Match already disposed its parser documents; both snapshots remain usable.
        Assert.Equal(30, difference.LeftValue!.Value.GetInt32());
        Assert.Equal(31, difference.RightValue!.Value.GetInt32());
        Assert.Contains("$.age", difference.ToString());
        Assert.Throws<NotSupportedException>(() => ((IList<JJDifference>)result.Differences).Clear());
    }

    [Theory]
    [InlineData("basic")]
    [InlineData("nested")]
    [InlineData("duplicates")]
    [InlineData("emptyArray")]
    [InlineData("emptyObject")]
    public void Identical_documents_match_completely(string name)
    {
        var result = JJMatcher.Match(Input("Left", name), Input("Left", name));
        Consistent(result);
        Assert.True(result.IsMatch);
        Assert.Equal(100, result.Score);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void Types_empty_missing_and_null_remain_distinct()
    {
        var result = JJMatcher.Match(Input("Left", "emptyArray"), Input("Right", "emptyObject"));
        Consistent(result);
        Assert.Equal(0, result.Score);
        Assert.Equal(JJDifferenceKind.TypeMismatch, Assert.Single(result.Differences).Kind);
        Assert.Equal("$", result.Differences[0].Path);

        var nested = JJMatcher.Match(Input("Left", "nested"), Input("Right", "nested"));
        Assert.Equal(new[] { "$.profile.city", "$.old", "$.nullable", "$.new" }, nested.Differences.Select(d => d.Path));
        var nullDifference = nested.Differences.Single(d => d.Path == "$.nullable");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, nullDifference.LeftValue!.Value.ValueKind);
        Assert.Null(nullDifference.RightValue);
        Assert.Null(nullDifference.RightPath);
        var added = nested.Differences.Single(d => d.Kind == JJDifferenceKind.Added);
        Assert.Null(added.LeftValue);
        Assert.Equal(1, added.RightValue!.Value[0].GetInt32());
    }

    [Fact]
    public void Structural_option_ignores_values_but_not_types()
    {
        var result = JJMatcher.Match(Input("Left", "basic"), Input("Right", "basic"), JJMatchOptions.StructuralMatching);
        Assert.True(result.IsMatch);
        Assert.Equal(100, result.Score);
        Assert.Null(result.ValueMatchPercentage);
        var changedType = JJMatcher.Match(Core("Left", "normalizedValues", "numberValue"),
            Core("Right", "normalizedValues", "numberText"), JJMatchOptions.StructuralMatching);
        Assert.False(changedType.IsMatch);
        Assert.Equal(0, changedType.Score);
    }

    [Fact]
    public void Strict_and_shape_arrays_have_explicit_semantics()
    {
        // Element order plays no part: [1, 2, 3] against [3, 2, 1] pairs every element with its equal.
        var strict = JJMatcher.Match(Input("Left", "arrayValues"), Input("Right", "arrayValues"));
        Consistent(strict);
        Assert.True(strict.IsMatch);
        var shapes = JJMatcher.Match(Input("Left", "shapeValues"), Input("Right", "shapeValues"),
            settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.StructureOnly });
        Assert.True(shapes.IsMatch); // One shape on each side, values and multiplicity ignored inside the array.
        // Strict keeps values and counts: the arrays and one element object match, two of five and three nodes.
        var counted = JJMatcher.Match(Input("Left", "shapeValues"), Input("Right", "shapeValues"));
        Assert.Equal(50, counted.Score);
        Assert.Equal(new[] { (JJDifferenceKind.ValueMismatch, "$[0].id"), (JJDifferenceKind.Missing, "$[1]") },
            counted.Differences.Select(d => (d.Kind, d.Path)));
    }

    [Fact]
    public void Count_preserving_arrays_report_surplus_and_use_both_sizes()
    {
        var result = JJMatcher.Match(Input("Left", "surplus"), Input("Right", "surplus"));
        Consistent(result);
        Assert.Equal(600d / 7, result.Score); // Three matched nodes; sizes three and four.
        var difference = Assert.Single(result.Differences);
        Assert.Equal(JJDifferenceKind.Added, difference.Kind);
        Assert.Equal("$[2]", difference.RightPath);
        Assert.Null(difference.LeftPath);
    }

    [Fact]
    public void Array_pairing_optimizes_the_complete_group_and_reports_original_indices()
    {
        var result = JJMatcher.Match(Input("Left", "arrayObjects"), Input("Right", "arrayObjects"));
        Assert.Equal(600d / 7, result.Score); // Six of seven nodes match on both sides.
        var difference = Assert.Single(result.Differences);
        Assert.Equal("$[0].name", difference.LeftPath);
        Assert.Equal("$[1].name", difference.RightPath);
        Assert.Equal("A", difference.LeftValue!.Value.GetString());
        Assert.Equal("X", difference.RightValue!.Value.GetString());
    }

    [Fact]
    public void Duplicate_occurrences_are_aligned_without_losing_location_context()
    {
        var result = JJMatcher.Match(Input("Left", "duplicates"), Input("Right", "duplicates"));
        Assert.Equal(80, result.Score);
        var difference = Assert.Single(result.Differences);
        Assert.Equal("$.entry#1.value", difference.LeftPath);
        Assert.Equal("$.entry#2.value", difference.RightPath);
        Assert.Equal(1, difference.LeftValue!.Value.GetInt32());
        Assert.Equal(3, difference.RightValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData("numericValues", "integerDecimal", JJValueMatchingMode.Numeric)]
    [InlineData("numericValues", "extremeExponent", JJValueMatchingMode.Numeric)]
    [InlineData("normalizedValues", "casing", JJValueMatchingMode.Normalized)]
    [InlineData("fuzzyValues", "typo", JJValueMatchingMode.Fuzzy)]
    public void Value_rules_work_through_the_public_api(string section, string name, JJValueMatchingMode mode)
    {
        var left = Core("Left", section, name);
        var right = Core("Right", section, name);
        Assert.False(JJMatcher.Match(left, right).IsMatch);
        var result = JJMatcher.Match(left, right, settings: new JJMatchSettings { ValueMode = mode });
        Consistent(result);
        Assert.True(result.IsMatch);
        // Identical values match under every rule: a document of strings, numbers, a Boolean and a null matches itself.
        var mixed = Input("Left", "guideScenario");
        Assert.True(JJMatcher.Match(mixed, mixed, settings: new JJMatchSettings { ValueMode = mode }).IsMatch);
    }

    [Fact]
    public void Fuzzy_threshold_setting_overrides_the_default_similarity()
    {
        Assert.Equal(0.8, new JJMatchSettings().FuzzyThreshold);
        var fuzzy = new JJMatchSettings { ValueMode = JJValueMatchingMode.Fuzzy };
        // "Jon" against "John" is a similarity of 0.75, under the default; "Smith" against "Smyth" is exactly 0.8.
        var shortName = (Core("Left", "fuzzyValues", "shortName"), Core("Right", "fuzzyValues", "shortName"));
        var boundary = (Core("Left", "fuzzyValues", "boundary"), Core("Right", "fuzzyValues", "boundary"));
        Assert.False(JJMatcher.Match(shortName.Item1, shortName.Item2, settings: fuzzy).IsMatch);
        Assert.True(JJMatcher.Match(boundary.Item1, boundary.Item2, settings: fuzzy).IsMatch);

        var relaxed = new JJMatchSettings { ValueMode = JJValueMatchingMode.Fuzzy, FuzzyThreshold = 0.75 };
        var result = JJMatcher.Match(shortName.Item1, shortName.Item2, settings: relaxed);
        Consistent(result);
        Assert.True(result.IsMatch);

        var strict = new JJMatchSettings { ValueMode = JJValueMatchingMode.Fuzzy, FuzzyThreshold = 0.81 };
        Assert.False(JJMatcher.Match(boundary.Item1, boundary.Item2, settings: strict).IsMatch);
        // At 1 Fuzzy accepts only what Normalized accepts; the setting plays no part without the Fuzzy rule.
        Assert.False(JJMatcher.Match(boundary.Item1, boundary.Item2, settings: new JJMatchSettings { ValueMode = JJValueMatchingMode.Fuzzy, FuzzyThreshold = 1 }).IsMatch);
        Assert.False(JJMatcher.Match(shortName.Item1, shortName.Item2, settings: new JJMatchSettings { FuzzyThreshold = 0 }).IsMatch);
    }

    [Fact]
    public void Normalized_names_pair_properties_that_differ_in_case_padding_or_unicode_form()
    {
        Assert.Equal(JJPropertyNameMatchingMode.None, new JJMatchSettings().PropertyNameMode);
        var left = Input("Left", "propertyNames");
        var right = Input("Right", "propertyNames");
        // Ten nodes on each side. Under None only identical names pair, so only the root, the array and its two element objects pair.
        // id/ID, name/" Name ", café in both Unicode forms and sku/SKU/Sku each fold to one key under Normalized even
        // though PropertyNameMode is None here, so each such pair is one PropertyNameCaseMismatch instead of a full
        // Missing and a full Added; the matched node count, and so the score, is unchanged. postalCode and postal_code
        // do not fold together, so they stay a Missing and an Added.
        var exact = JJMatcher.Match(left, right);
        Consistent(exact);
        Assert.Equal(40, exact.Score);
        Assert.Equal(7, exact.Differences.Count);
        Assert.Equal(5, exact.Differences.Count(d => d.Kind == JJDifferenceKind.PropertyNameCaseMismatch));
        Assert.All(exact.Differences, d => Assert.NotEqual(JJDifferenceKind.ValueMismatch, d.Kind));

        // Weighted, default weights: the 5 case-only pairs weigh 2 nodes each at PropertyNameCaseMismatchWeight (4),
        // postalCode weighs 1 at MissingWeight (8) and postal_code 1 at AddedWeight (3): 5 × 2 × 4 + 8 + 3 = 51
        // against the matched mass 2 × 10 × 4 = 80.
        var weighted = JJMatcher.Match(left, right, settings: new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted } });
        Consistent(weighted);
        Assert.Equal(8000d / 131, weighted.Score);
        Assert.All(weighted.Differences.Where(d => d.Kind == JJDifferenceKind.PropertyNameCaseMismatch), d => Assert.Equal(4, d.ScoreWeight));
        // Had these five pairs stayed a full Missing and a full Added, the mass would be 5 × (8 + 3) + 8 + 3 = 66,
        // scoring lower: the new kind discounts a name-only rename instead of penalizing it like lost content.
        Assert.True(weighted.Score > 100d * 80 / (80 + 5 * 11 + 8 + 3));

        // id/ID, name/" Name ", café in both Unicode forms and the two skus pair; postalCode and postal_code do not.
        var settings = new JJMatchSettings { PropertyNameMode = JJPropertyNameMatchingMode.Normalized };
        var normalized = JJMatcher.Match(left, right, settings: settings);
        Consistent(normalized);
        Assert.Equal(90, normalized.Score);
        Assert.Equal(90, normalized.StructuralMatchPercentage);
        Assert.Equal(new[] { (JJDifferenceKind.Missing, "$.postalCode"), (JJDifferenceKind.Added, "$.postal_code") },
            normalized.Differences.Select(d => (d.Kind, d.Path)));
        Assert.Equal(normalized.Score, JJMatcher.Match(right, left, settings: settings).Score);
        Assert.Equal(90, JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching, settings).Score);

        // Under StructureOnly the two element objects of each side are one shape once their names fold together;
        // under None they are two shapes on each side, and only the two spelled "sku" can be paired.
        var leftItems = Fixture("Left").GetProperty("publicApi").GetProperty("propertyNames").GetProperty("items").GetRawText();
        var rightItems = Fixture("Right").GetProperty("publicApi").GetProperty("propertyNames").GetProperty("items").GetRawText();
        var shapes = JJMatcher.Match(leftItems, rightItems,
            settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.StructureOnly, PropertyNameMode = JJPropertyNameMatchingMode.Normalized });
        Consistent(shapes);
        Assert.True(shapes.IsMatch);
        var exactShapes = JJMatcher.Match(leftItems, rightItems, settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.StructureOnly });
        Assert.Equal(60, exactShapes.Score); // The arrays and the "sku" shape: three of five effective nodes on each side.
        Assert.Equal(new[] { JJDifferenceKind.Missing, JJDifferenceKind.Added }, exactShapes.Differences.Select(d => d.Kind));
    }

    [Theory]
    // These core.normalizedValues pairs fold to equal under Normalized (case, outer white space or Unicode form
    // only: "Masoud"/"masoud", "  Hello  "/"hello", "   "/"\t \n", "istanbul"/"ISTANBUL", "x\uD800y"/" X\uD800Y ").
    [InlineData("casing")]
    [InlineData("padding")]
    [InlineData("blankText")]
    [InlineData("turkish")]
    [InlineData("lonePadded")]
    public void Value_pairs_that_fold_equal_report_a_lighter_case_mismatch(string name)
    {
        var left = Core("Left", "normalizedValues", name);
        var right = Core("Right", "normalizedValues", name);
        var result = JJMatcher.Match(left, right);
        Consistent(result);
        Assert.False(result.IsMatch);
        var difference = Assert.Single(result.Differences);
        Assert.Equal(JJDifferenceKind.ValueCaseMismatch, difference.Kind);
        Assert.Equal(10, difference.ScoreWeight); // Under Standard every difference counts fully.
        // Confirms the pair is accepted once the relaxation it falls under is actually selected.
        Assert.True(JJMatcher.Match(left, right, settings: new JJMatchSettings { ValueMode = JJValueMatchingMode.Normalized }).IsMatch);

        var weighted = JJMatcher.Match(left, right, settings: new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted } });
        Consistent(weighted);
        Assert.Equal(4, Assert.Single(weighted.Differences).ScoreWeight); // ValueCaseMismatchWeight default, lighter than ValueMismatchWeight (6).
    }

    [Theory]
    // These core.normalizedValues pairs differ by more than case, outer white space or Unicode form, so they do
    // not fold to equal under Normalized and stay a full value mismatch.
    [InlineData("innerSpace")]
    [InlineData("punctuated")]
    [InlineData("dottedCapital")]
    [InlineData("dotlessSmall")]
    [InlineData("loneComposed")]
    public void Value_pairs_that_differ_beyond_case_stay_a_full_value_mismatch(string name)
    {
        var left = Core("Left", "normalizedValues", name);
        var right = Core("Right", "normalizedValues", name);
        var result = JJMatcher.Match(left, right);
        Consistent(result);
        Assert.False(result.IsMatch);
        Assert.Equal(JJDifferenceKind.ValueMismatch, Assert.Single(result.Differences).Kind);
        Assert.False(JJMatcher.Match(left, right, settings: new JJMatchSettings { ValueMode = JJValueMatchingMode.Normalized }).IsMatch);
    }

    [Fact]
    public void Normalized_names_make_case_variants_one_duplicate_group_and_keep_original_spellings_in_paths()
    {
        var variants = Fixture("Left").GetProperty("core").GetProperty("caseVariants").GetRawText();
        var settings = new JJMatchSettings { PropertyNameMode = JJPropertyNameMatchingMode.Normalized };
        Assert.True(JJMatcher.Match(variants, variants, settings: settings).IsMatch);

        // { name: 1, Name: 2, NAME: { name: 3 } } against { name: "Ann", age: 30 }: three occurrences of one name
        // against one. No candidate pair matches a node, so the first left occurrence takes the only partner and
        // the other two are surplus, reported under their own spelling without an occurrence suffix.
        var result = JJMatcher.Match(variants, Input("Left", "basic"), JJMatchOptions.StructuralMatching, settings);
        Consistent(result);
        Assert.Equal(25, result.Score); // Only the roots match: one of five plus three nodes.
        Assert.Equal(new (JJDifferenceKind Kind, string? LeftPath, string? RightPath)[]
            {
                (JJDifferenceKind.TypeMismatch, "$.name", "$.name"),
                (JJDifferenceKind.Missing, "$.Name", null),
                (JJDifferenceKind.Missing, "$.NAME", null),
                (JJDifferenceKind.Added, null, "$.age"),
            },
            result.Differences.Select(d => (d.Kind, d.LeftPath, d.RightPath)));
    }

    [Fact]
    public void Fuzzy_arrays_use_a_bijection_not_transitive_grouping()
    {
        var result = JJMatcher.Match(Input("Left", "fuzzyAssignment"), Input("Right", "fuzzyAssignment"),
            settings: new JJMatchSettings { ValueMode = JJValueMatchingMode.Fuzzy });
        // The first left string matches only the second right string; a greedy exact-first choice loses a match.
        Assert.True(result.IsMatch);
        Assert.Equal(100, result.Score);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void Stringified_object_or_array_root_is_decoded_and_other_string_roots_stay_literal()
    {
        var json = File.ReadAllText(Path.Combine(TestDataDirectory, "Left.json"));
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Stringified", "LeftStringify.txt"));
        var settings = new JJMatchSettings { MaxDepth = 128 };
        Assert.True(JJMatcher.Match(text, json, settings: settings).IsMatch);
        Assert.True(JJMatcher.Match(json, text, settings: settings).IsMatch);
        Assert.False(JJMatcher.Match(Input("Left", "nestedStringified"), Input("Right", "nestedStringified"), settings: settings).IsMatch);
        // "{" and a lone surrogate are string roots that cannot be decoded as JSON: they compare as the strings they are.
        var invalidText = Input("Left", "invalidText");
        Assert.True(JJMatcher.Match(invalidText, invalidText).IsMatch);
        Assert.Equal(JJDifferenceKind.ValueMismatch, Assert.Single(JJMatcher.Match(invalidText, Input("Right", "invalidText")).Differences).Kind);
        var loneSurrogate = Fixture("Left").GetProperty("core").GetProperty("loneSurrogates").GetProperty("values")[0].GetRawText();
        Assert.True(JJMatcher.Match(loneSurrogate, loneSurrogate).IsMatch);
        Assert.Equal(JJDifferenceKind.TypeMismatch, Assert.Single(JJMatcher.Match(loneSurrogate, json, settings: settings).Differences).Kind);
        // Only objects and arrays are decoded: the text of a number stays a string against the number itself.
        var normalized = Fixture("Left").GetProperty("core").GetProperty("normalizedValues");
        var numberText = JJMatcher.Match(normalized.GetProperty("numberText").GetRawText(), normalized.GetProperty("numberValue").GetRawText());
        Assert.Equal(JJDifferenceKind.TypeMismatch, Assert.Single(numberText.Differences).Kind);
        // Decoding is not normalization: the decoded fixtures report what the plain fixtures report.
        var rightText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Stringified", "RightStringify.txt"));
        var plain = JJMatcher.Match(json, File.ReadAllText(Path.Combine(TestDataDirectory, "Right.json")), settings: settings);
        var decoded = JJMatcher.Match(text, rightText, settings: settings);
        Assert.Equal(plain.Score, decoded.Score);
        Assert.Equal(plain.Differences.Select(d => (d.Kind, d.LeftPath, d.RightPath)), decoded.Differences.Select(d => (d.Kind, d.LeftPath, d.RightPath)));
        // Content deeper than MaxDepth does not parse, so the string stays literal instead of throwing.
        var shallow = new JJMatchSettings { MaxDepth = 64 };
        Assert.True(JJMatcher.Match(text, text, settings: shallow).IsMatch);
        Assert.Equal(JJDifferenceKind.ValueMismatch, Assert.Single(JJMatcher.Match(text, rightText, settings: shallow).Differences).Kind);
    }

    [Fact]
    public void Stringified_object_or_array_value_is_compared_as_the_json_it_holds()
    {
        // payload and reordered hold the same JSON on both sides, written with other white space or in another
        // property and element order; changed is reordered too, but its status differs. Decoded, that one value is
        // the only difference, not the whole string, and its path continues through the decoded document.
        var result = JJMatcher.Match(Input("Left", "nestedStringified"), Input("Right", "nestedStringified"));
        Consistent(result);
        Assert.False(result.IsMatch);
        var changed = Assert.Single(result.Differences);
        Assert.Equal(JJDifferenceKind.ValueMismatch, changed.Kind);
        Assert.Equal("$.changed.status", changed.LeftPath);
        Assert.Equal("$.changed.status", changed.RightPath);
        Assert.Equal("open", changed.LeftValue!.Value.GetString());
        Assert.Equal("closed", changed.RightValue!.Value.GetString());
    }

    [Theory]
    [InlineData("No real difference: property order")]
    [InlineData("Array order: elements reversed")]
    public void Stringified_root_from_the_change_catalogue_is_compared_as_the_document_it_holds(string category)
    {
        // The catalogue strings hold a complete JSON object or array. As literal strings none of these pairs is equal;
        // decoded, they follow the object and array rules, under which neither property nor element order matters.
        foreach (var (inA, inB) in Catalogue(category))
        {
            var result = JJMatcher.Match(inA, inB);
            Consistent(result);
            Assert.True(result.IsMatch);
            Assert.True(JJMatcher.Match(inB, inA).IsMatch);
        }
    }

    [Fact]
    public void Stringified_root_that_does_not_parse_stays_a_literal_string()
    {
        // These catalogue excerpts are not complete JSON, although two start with '[' or '{': compared as strings,
        // each matches itself and differs from its pair at the root, and nothing throws.
        foreach (var category in new[] { "Structure: array shorter", "Array order: primitives reordered", "Array order: objects swapped" })
        {
            foreach (var (inA, inB) in Catalogue(category))
            {
                Assert.True(JJMatcher.Match(inA, inA).IsMatch);
                var difference = Assert.Single(JJMatcher.Match(inA, inB).Differences);
                Assert.Equal(JJDifferenceKind.ValueMismatch, difference.Kind);
                Assert.Equal("$", difference.LeftPath);
            }
        }
    }

    // The raw JSON text of the two strings of every stressChanges entry with the given category. The strings are
    // passed as they are stored, so each is a JSON document whose root is a string.
    private static List<(string InA, string InB)> Catalogue(string category)
    {
        var pairs = Fixture("Left").GetProperty("stressChanges").EnumerateArray()
            .Where(e => e.GetProperty("category").GetString() == category)
            .Select(e => (e.GetProperty("inA").GetRawText(), e.GetProperty("inB").GetRawText()))
            .ToList();
        Assert.NotEmpty(pairs);
        return pairs;
    }

    [Fact]
    public void Errors_and_limits_never_return_a_partial_result()
    {
        var valid = Input("Left", "basic");
        Assert.Throws<ArgumentNullException>(() => JJMatcher.Match(null!, valid));
        Assert.Throws<ArgumentNullException>(() => JJMatcher.Match(valid, null!));
        var malformed = Fixture("Left").GetProperty("publicApi").GetProperty("invalidText").GetString()!;
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JJMatcher.Match(malformed, valid));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JJMatcher.Match(valid, malformed));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, (JJMatchOptions)128));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { ValueMode = (JJValueMatchingMode)16 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { PropertyNameMode = (JJPropertyNameMatchingMode)2 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { FuzzyThreshold = -0.1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { FuzzyThreshold = 1.1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { FuzzyThreshold = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxDepth = 0 }));
        // The depth has no upper bound: the parser's former cap of 128 is accepted and above.
        Assert.True(JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxDepth = 129 }).IsMatch);
        Assert.True(JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxDepth = int.MaxValue }).IsMatch);
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { ArrayMode = (JJArrayMatchingMode)2 }));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxInputLength = 1 }));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxNodes = 1 }));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxWork = 1 }));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(Input("Left", "arrayValues"), Input("Right", "arrayValues"),
            settings: new JJMatchSettings { MaxAssignmentSize = 2 }));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JJMatcher.Match(Input("Left", "nested"), valid,
            settings: new JJMatchSettings { MaxDepth = 1 }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => JJMatcher.Match(valid, valid, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Scoring_configuration_is_always_present_and_defaults_to_standard_scoring()
    {
        var settings = new JJMatchSettings();
        Assert.NotNull(settings.Scoring);
        Assert.NotSame(settings.Scoring, new JJMatchSettings().Scoring);
        Assert.Equal(JJScoringMode.Standard, settings.Scoring.Mode);
        Assert.Equal(JJScoringMode.Standard, default(JJScoringMode));
        Assert.Equal(1, JJScoringSettings.MinWeight);
        Assert.Equal(10, JJScoringSettings.MaxWeight);
        Assert.Equal((10, 8, 6, 3, 4, 4), (settings.Scoring.TypeMismatchWeight, settings.Scoring.MissingWeight,
            settings.Scoring.ValueMismatchWeight, settings.Scoring.AddedWeight, settings.Scoring.PropertyNameCaseMismatchWeight,
            settings.Scoring.ValueCaseMismatchWeight));
        Assert.Throws<ArgumentNullException>(() => settings.Scoring = null!);
        Assert.NotNull(settings.Scoring);

        // Under Standard the weights play no part, so a settings object with any weights scores like the default.
        var left = Input("Left", "nested");
        var right = Input("Right", "nested");
        var standard = JJMatcher.Match(left, right);
        var unused = new JJMatchSettings { Scoring = { MissingWeight = 1, AddedWeight = 10, TypeMismatchWeight = 1, ValueMismatchWeight = 1, PropertyNameCaseMismatchWeight = 1, ValueCaseMismatchWeight = 1 } };
        Assert.Equal(standard.Score, JJMatcher.Match(left, right, settings: unused).Score);
        // The comparison copies the configuration: validation does not touch the caller's object.
        Assert.Equal(JJScoringMode.Standard, unused.Scoring.Mode);
        Assert.Equal(10, unused.Scoring.AddedWeight);
    }

    [Fact]
    public void Weighted_scoring_discounts_each_kind_by_its_weight_and_each_subtree_by_its_size()
    {
        // nested: 8 nodes on each side; the root, profile, tags and its two elements match. Unmatched: city (2 nodes)
        // as a value mismatch, old (1) and nullable (1) missing, new (array + 1 element) added: 6 nodes, which is
        // 16 - 2 × 5. Standard: 1000 / 16. Default weights: 2 × 6 + 2 × 8 + 2 × 3 = 34 against 2 × 10 × 5.
        var left = Input("Left", "nested");
        var right = Input("Right", "nested");
        var weighted = new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted } };
        var result = JJMatcher.Match(left, right, settings: weighted);
        Consistent(result);
        Assert.Equal(62.5, JJMatcher.Match(left, right).Score);
        Assert.Equal(10000d / 134, result.Score);
        Assert.Equal(result.Score, result.ValueMatchPercentage);
        // Each difference reports the weight applied to it: the configured weight of its kind.
        Assert.Equal(new[] { ("$.profile.city", 6), ("$.old", 8), ("$.nullable", 8), ("$.new", 3) },
            result.Differences.Select(d => (d.Path, d.ScoreWeight)));
        // The added array weighs its two nodes: 7 more per node when its weight goes from 3 to 10, 2 less at 1.
        weighted.Scoring.AddedWeight = 10;
        var heavierAdded = JJMatcher.Match(left, right, settings: weighted);
        Assert.Equal(10000d / 148, heavierAdded.Score);
        Assert.Equal(10, heavierAdded.Differences.Single(d => d.Kind == JJDifferenceKind.Added).ScoreWeight);
        weighted.Scoring.AddedWeight = 1;
        Assert.Equal(10000d / 130, JJMatcher.Match(left, right, settings: weighted).Score);
        // The structural pass has no value mismatches: old, nullable and new remain, 8 + 8 + 2 against 2 × 10 × 6.
        Assert.Equal(12000d / 138, JJMatcher.Match(left, right, settings: weighted).StructuralMatchPercentage);
        Assert.Equal(12000d / 138, JJMatcher.Match(left, right, JJMatchOptions.StructuralMatching, weighted).Score);

        // The guide pair under the defaults: 14 matched, 4 full value mismatches of 2 nodes (customer.name folds to
        // equal under Normalized, so it is one lighter ValueCaseMismatch instead), legacyCode missing (1), coupon
        // added (1): 4 × 2 × 6 + 2 × 4 + 8 + 3 = 67 against 280. The structural diagnostic ignores values entirely,
        // so it is unaffected: 19 matched, 8 + 3 against 380.
        var guide = JJMatcher.Match(Input("Left", "guideScenario"), Input("Right", "guideScenario"),
            settings: new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted } });
        Consistent(guide);
        Assert.Equal(28000d / 347, guide.Score);
        Assert.Equal(38000d / 391, guide.StructuralMatchPercentage);

        // A weight of 10 counts a lost node exactly as Standard does: basic has one value mismatch and nothing else.
        var basic = new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted, ValueMismatchWeight = 10 } };
        Assert.Equal(200d / 3, JJMatcher.Match(Input("Left", "basic"), Input("Right", "basic"), settings: basic).Score);
        basic.Scoring.ValueMismatchWeight = 1;
        Assert.Equal(4000d / 42, JJMatcher.Match(Input("Left", "basic"), Input("Right", "basic"), settings: basic).Score);
        // Nothing matched is 0 whatever the weights: the roots are of different types.
        Assert.Equal(0, JJMatcher.Match(Input("Left", "emptyArray"), Input("Right", "emptyObject"),
            settings: new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted, TypeMismatchWeight = 1 } }).Score);
        // The one added element of surplus is an Added on the way there and a Missing on the way back: 3 against 8.
        var surplus = new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted } };
        Assert.Equal(6000d / 63, JJMatcher.Match(Input("Left", "surplus"), Input("Right", "surplus"), settings: surplus).Score);
        Assert.Equal(6000d / 68, JJMatcher.Match(Input("Right", "surplus"), Input("Left", "surplus"), settings: surplus).Score);
    }

    [Fact]
    public void Weighted_scoring_keeps_differences_and_match_decisions_and_agrees_with_standard_at_the_maximum_weight()
    {
        foreach (var mode in Enum.GetValues<JJArrayMatchingMode>())
        {
            foreach (var name in new[] { "guideScenario", "basic", "nested", "arrayValues", "arrayObjects", "surplus", "duplicates", "shapeValues", "propertyNames", "emptyArray" })
            {
                var left = Input("Left", name);
                var right = Input("Right", name == "emptyArray" ? "emptyObject" : name);
                foreach (var options in new[] { JJMatchOptions.All, JJMatchOptions.StructuralMatching, JJMatchOptions.ValueMatching })
                {
                    var standard = JJMatcher.Match(left, right, options, new JJMatchSettings { ArrayMode = mode });
                    var maximum = JJMatcher.Match(left, right, options, new JJMatchSettings
                    {
                        ArrayMode = mode,
                        Scoring = { Mode = JJScoringMode.Weighted, MissingWeight = 10, AddedWeight = 10, TypeMismatchWeight = 10, ValueMismatchWeight = 10, PropertyNameCaseMismatchWeight = 10, ValueCaseMismatchWeight = 10 }
                    });
                    var defaults = new JJMatchSettings { ArrayMode = mode, Scoring = { Mode = JJScoringMode.Weighted } };
                    var weighted = JJMatcher.Match(left, right, options, defaults);
                    Consistent(maximum);
                    Consistent(weighted);
                    // Every weight at the maximum is the Standard score, bit for bit, in both result percentages.
                    Assert.Equal(standard.Score, maximum.Score);
                    Assert.Equal(standard.StructuralMatchPercentage, maximum.StructuralMatchPercentage);
                    Assert.Equal(standard.ValueMatchPercentage, maximum.ValueMatchPercentage);
                    // Weights discount, never amplify; the match decision and the differences never move.
                    Assert.True(weighted.Score >= standard.Score);
                    Assert.Equal(standard.IsMatch, weighted.IsMatch);
                    Assert.Equal(standard.Differences.Select(d => (d.Kind, d.LeftPath, d.RightPath)),
                        weighted.Differences.Select(d => (d.Kind, d.LeftPath, d.RightPath)));
                    // ScoreWeight is the weight applied: 10 for every difference under Standard, the kind's weight under Weighted.
                    Assert.All(standard.Differences, d => Assert.Equal(10, d.ScoreWeight));
                    Assert.All(weighted.Differences, d => Assert.Equal(d.Kind switch
                    {
                        JJDifferenceKind.Missing => 8,
                        JJDifferenceKind.Added => 3,
                        JJDifferenceKind.TypeMismatch => 10,
                        JJDifferenceKind.PropertyNameCaseMismatch => 4,
                        JJDifferenceKind.ValueCaseMismatch => 4,
                        _ => 6
                    }, d.ScoreWeight));
                    Assert.Equal(weighted.Score, JJMatcher.Match(left, right, options, defaults).Score);
                    // Swapping the documents turns every Missing into an Added, so the score is symmetric when the two
                    // weights travel with the documents, and whenever they are equal.
                    var mirrored = new JJMatchSettings { ArrayMode = mode, Scoring = { Mode = JJScoringMode.Weighted, MissingWeight = 3, AddedWeight = 8 } };
                    Assert.Equal(weighted.Score, JJMatcher.Match(right, left, options, mirrored).Score);
                    var even = new JJMatchSettings { ArrayMode = mode, Scoring = { Mode = JJScoringMode.Weighted, MissingWeight = 5, AddedWeight = 5 } };
                    Assert.Equal(JJMatcher.Match(left, right, options, even).Score, JJMatcher.Match(right, left, options, even).Score);
                    // A kind that is not reported has no effect on the score.
                    if (weighted.Differences.All(d => d.Kind != JJDifferenceKind.TypeMismatch))
                    {
                        defaults.Scoring.TypeMismatchWeight = 1;
                        Assert.Equal(weighted.Score, JJMatcher.Match(left, right, options, defaults).Score);
                    }
                }
            }
        }
    }

    [Fact]
    public void Scoring_weights_outside_the_scale_and_unknown_modes_are_rejected()
    {
        var valid = Input("Left", "basic");
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { Mode = (JJScoringMode)2 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { MissingWeight = 0 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { AddedWeight = 11 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { TypeMismatchWeight = -1 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { ValueMismatchWeight = int.MaxValue } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { PropertyNameCaseMismatchWeight = 0 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { ValueCaseMismatchWeight = 0 } }));
        // The weights are validated under Standard too, so a configuration is either valid or rejected whatever the mode.
        Assert.True(JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { MissingWeight = 1, AddedWeight = 10 } }).IsMatch);
        Assert.Equal(100, JJMatcher.Match(valid, valid, settings: new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted } }).Score);
    }

    [Fact]
    public void Options_keep_differences_mandatory_and_scores_symmetric_and_deterministic()
    {
        foreach (var mode in Enum.GetValues<JJArrayMatchingMode>())
        {
            foreach (var nameMode in Enum.GetValues<JJPropertyNameMatchingMode>())
            {
                var settings = new JJMatchSettings { ArrayMode = mode, PropertyNameMode = nameMode };
                foreach (var name in new[] { "basic", "nested", "arrayValues", "arrayObjects", "surplus", "duplicates", "shapeValues", "propertyNames" })
                {
                    var left = Input("Left", name);
                    var right = Input("Right", name);
                    foreach (var options in new[] { JJMatchOptions.All, JJMatchOptions.StructuralMatching, JJMatchOptions.ValueMatching,
                        JJMatchOptions.StructuralMatching | JJMatchOptions.ValueMatching })
                    {
                        var result = JJMatcher.Match(left, right, options, settings);
                        var reverse = JJMatcher.Match(right, left, options, settings);
                        var repeated = JJMatcher.Match(left, right, options, settings);
                        Consistent(result);
                        Consistent(reverse);
                        Assert.Equal(result.Score, reverse.Score);
                        Assert.Equal(result.IsMatch, reverse.IsMatch);
                        Assert.Equal(result.StructuralMatchPercentage, reverse.StructuralMatchPercentage);
                        Assert.Equal(result.Score, repeated.Score);
                        Assert.Equal(result.Differences.Select(d => (d.Kind, d.LeftPath, d.RightPath)),
                            repeated.Differences.Select(d => (d.Kind, d.LeftPath, d.RightPath)));
                    }
                }
            }
        }
    }
}
