namespace JJMatch.Tests;

public class JJMatcherIntegrationTests
{
    // Guide.md uses this exact pair in every row. Counts below are hand-tallied from that document,
    // independent of the production tree, shape signatures and assignment engine.
    [Theory]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Exact, JJMatchOptions.All, 10, 20, 9)]
    [InlineData(JJArrayMatchingMode.Unordered, JJValueMatchingMode.Exact, JJMatchOptions.All, 14, 20, 5)]
    [InlineData(JJArrayMatchingMode.StructureOnly, JJValueMatchingMode.Exact, JJMatchOptions.All, 11, 17, 5)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Numeric, JJMatchOptions.All, 7, 20, 12)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Normalized, JJMatchOptions.All, 11, 20, 8)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Fuzzy, JJMatchOptions.All, 12, 20, 7)]
    [InlineData(JJArrayMatchingMode.Unordered, JJValueMatchingMode.Exact | JJValueMatchingMode.Numeric | JJValueMatchingMode.Normalized, JJMatchOptions.All, 16, 20, 3)]
    [InlineData(JJArrayMatchingMode.Strict, JJValueMatchingMode.Exact, JJMatchOptions.StructuralMatching, 19, 20, 0)]
    [InlineData(JJArrayMatchingMode.Unordered, JJValueMatchingMode.Exact | JJValueMatchingMode.Numeric | JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy, JJMatchOptions.All, 17, 20, 2)]
    public void Guide_configuration_table_matches_independently_counted_nodes(
        JJArrayMatchingMode arrayMode, JJValueMatchingMode valueMode, JJMatchOptions options,
        int matchedNodes, int effectiveNodesPerSide, int valueDifferences)
    {
        var left = Input("Left", "guideScenario");
        var right = Input("Right", "guideScenario");
        var settings = new JJMatchSettings { ArrayMode = arrayMode, ValueMode = valueMode };
        var result = JJMatcher.Match(left, right, options, settings);
        Consistent(result);
        Assert.False(result.IsMatch);
        Assert.Equal(200d * matchedNodes / (2 * effectiveNodesPerSide), result.Score);
        Assert.Equal(valueDifferences + 2, result.Differences.Count);
        Assert.Equal(valueDifferences, result.Differences.Count(d => d.Kind == JJDifferenceKind.ValueMismatch));
        Assert.Equal("$.legacyCode", Assert.Single(result.Differences.Where(d => d.Kind == JJDifferenceKind.Missing)).Path);
        Assert.Equal("$.coupon", Assert.Single(result.Differences.Where(d => d.Kind == JJDifferenceKind.Added)).Path);
        Assert.Equal(result.Score, JJMatcher.Match(right, left, options, settings).Score);
    }

    [Theory]
    [InlineData("items", JJArrayMatchingMode.Unordered, JJValueMatchingMode.Exact, JJMatchOptions.All, 300d / 7, 4)]
    [InlineData("items", JJArrayMatchingMode.StructureOnly, JJValueMatchingMode.Exact, JJMatchOptions.All, 300d / 7, 4)]
    [InlineData("total", JJArrayMatchingMode.Strict, JJValueMatchingMode.Exact | JJValueMatchingMode.Numeric, JJMatchOptions.All, 0, 1)]
    [InlineData("customer.name", JJArrayMatchingMode.Strict, JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized, JJMatchOptions.All, 0, 1)]
    [InlineData("customer.contact", JJArrayMatchingMode.Strict, JJValueMatchingMode.Exact | JJValueMatchingMode.Fuzzy, JJMatchOptions.All, 0, 1)]
    [InlineData("customer", JJArrayMatchingMode.Strict, JJValueMatchingMode.Exact | JJValueMatchingMode.Fuzzy, JJMatchOptions.All, 50, 2)]
    [InlineData("paid", JJArrayMatchingMode.Strict, JJValueMatchingMode.Exact, JJMatchOptions.StructuralMatching, 0, 1)]
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
        Assert.All(before.Differences, d => Assert.Equal(JJDifferenceKind.ValueMismatch, d.Kind));
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
    public void Array_matching_mode_defaults_to_structure_only_and_the_other_modes_are_distinct()
    {
        Assert.Equal(JJArrayMatchingMode.StructureOnly, default(JJArrayMatchingMode));
        Assert.Equal(JJArrayMatchingMode.Strict, new JJMatchSettings().ArrayMode);

        Assert.Equal(
            [JJArrayMatchingMode.StructureOnly, JJArrayMatchingMode.Unordered, JJArrayMatchingMode.Strict],
            Enum.GetValues<JJArrayMatchingMode>());

        Assert.Equal(1, (int)JJArrayMatchingMode.Unordered);
        Assert.Equal(2, (int)JJArrayMatchingMode.Strict);
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
        var left = Input("Left", "arrayValues");
        var right = Input("Right", "arrayValues");
        var settings = new JJMatchSettings { ArrayMode = JJArrayMatchingMode.Unordered };
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
        Assert.Equal(JJArrayMatchingMode.Unordered, settings.ArrayMode);
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
        Assert.Equal(new[] { "$.profile.city", "$.tags[0]", "$.tags[1]", "$.old", "$.nullable", "$.new" },
            nested.Differences.Select(d => d.Path));
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
    public void Strict_unordered_and_shape_arrays_have_explicit_semantics()
    {
        var left = Input("Left", "arrayValues");
        var right = Input("Right", "arrayValues");
        var strict = JJMatcher.Match(left, right);
        Assert.Equal(50, strict.Score); // Array and middle element out of four nodes.
        Assert.Equal(new[] { "$[0]", "$[2]" }, strict.Differences.Select(d => d.Path));
        var unordered = JJMatcher.Match(left, right, settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.Unordered });
        Assert.True(unordered.IsMatch);
        var shapes = JJMatcher.Match(Input("Left", "shapeValues"), Input("Right", "shapeValues"),
            settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.StructureOnly });
        Assert.True(shapes.IsMatch); // One shape on each side, values and multiplicity ignored inside the array.
        Assert.False(JJMatcher.Match(Input("Left", "shapeValues"), Input("Right", "shapeValues")).IsMatch);
    }

    [Theory]
    [InlineData(JJArrayMatchingMode.Strict)]
    [InlineData(JJArrayMatchingMode.Unordered)]
    public void Count_preserving_arrays_report_surplus_and_use_both_sizes(JJArrayMatchingMode mode)
    {
        var settings = new JJMatchSettings { ArrayMode = mode };
        var result = JJMatcher.Match(Input("Left", "surplus"), Input("Right", "surplus"), settings: settings);
        Consistent(result);
        Assert.Equal(600d / 7, result.Score); // Three matched nodes; sizes three and four.
        var difference = Assert.Single(result.Differences);
        Assert.Equal(JJDifferenceKind.Added, difference.Kind);
        Assert.Equal("$[2]", difference.RightPath);
        Assert.Null(difference.LeftPath);
    }

    [Fact]
    public void Unordered_pairing_optimizes_the_complete_group_and_reports_original_indices()
    {
        var result = JJMatcher.Match(Input("Left", "arrayObjects"), Input("Right", "arrayObjects"),
            settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.Unordered });
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
    }

    [Fact]
    public void Unordered_fuzzy_arrays_use_a_bijection_not_transitive_grouping()
    {
        var result = JJMatcher.Match(Input("Left", "fuzzyAssignment"), Input("Right", "fuzzyAssignment"),
            settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.Unordered, ValueMode = JJValueMatchingMode.Fuzzy });
        // The first left string matches only the second right string; a greedy exact-first choice loses a match.
        Assert.True(result.IsMatch);
        Assert.Equal(100, result.Score);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void Stringified_input_is_opt_in_and_nested_strings_stay_literal()
    {
        var json = File.ReadAllText(Path.Combine(TestDataDirectory, "Left.json"));
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Stringified", "LeftStringify.txt"));
        var settings = new JJMatchSettings { MaxDepth = 128, ParseStringifiedJson = true };
        Assert.True(JJMatcher.Match(text, json, settings: settings).IsMatch);
        Assert.True(JJMatcher.Match(json, text, settings: settings).IsMatch);
        Assert.False(JJMatcher.Match(text, json, settings: new JJMatchSettings { MaxDepth = 128 }).IsMatch);
        Assert.False(JJMatcher.Match(Input("Left", "nestedStringified"), Input("Right", "nestedStringified"), settings: settings).IsMatch);
        var loneSurrogate = Fixture("Left").GetProperty("core").GetProperty("loneSurrogates").GetProperty("values")[0].GetRawText();
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JJMatcher.Match(loneSurrogate, json, settings: settings));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JJMatcher.Match(Input("Left", "invalidText"), json, settings: settings));
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
        Assert.Throws<ArgumentException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { ValueMode = JJValueMatchingMode.None }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxDepth = 129 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { ArrayMode = (JJArrayMatchingMode)9 }));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxInputLength = 1 }));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxNodes = 1 }));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(valid, valid, settings: new JJMatchSettings { MaxWork = 1 }));
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(Input("Left", "arrayValues"), Input("Right", "arrayValues"),
            settings: new JJMatchSettings { ArrayMode = JJArrayMatchingMode.Unordered, MaxAssignmentSize = 2 }));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JJMatcher.Match(Input("Left", "nested"), valid,
            settings: new JJMatchSettings { MaxDepth = 1 }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => JJMatcher.Match(valid, valid, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Options_keep_differences_mandatory_and_scores_symmetric_and_deterministic()
    {
        foreach (var mode in Enum.GetValues<JJArrayMatchingMode>())
        {
            var settings = new JJMatchSettings { ArrayMode = mode };
            foreach (var name in new[] { "basic", "nested", "arrayValues", "arrayObjects", "surplus", "duplicates", "shapeValues" })
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
