using System.Text.Json;

namespace JJMatch.Tests;

// Uses the existing approved documents; configuration round trips exercise the exported artifact itself.
public class SimpleConfigurationTests
{
    private static string Input(string side, string section = "guideScenario")
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "ComplexJson", side + ".json")), new JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.GetProperty("publicApi").GetProperty(section).GetRawText();
    }

    [Fact]
    public void Presets_keep_independent_expected_scores_and_are_fresh_instances()
    {
        var left = Input("Left");
        var right = Input("Right");
        Assert.Equal(70, JJMatcher.Match(left, right, settings: JJMatchSettings.Default).Score);
        Assert.Equal(80, JJMatcher.Match(left, right, settings: JJMatchSettings.Normalized).Score);
        var structure = JJMatcher.Match(left, right, settings: JJMatchSettings.StructureOnly);
        Assert.Equal(95, structure.Score);
        Assert.Equal(2, structure.Differences.Count);
        Assert.Null(structure.Diagnostics.ValueScore);
        var modified = JJMatchSettings.Normalized;
        modified.StringComparison = JJStringComparison.Fuzzy;
        modified.Limits.MaxNodes = 1;
        modified.Scoring.MissingWeight = 1;
        var fresh = JJMatchSettings.Normalized;
        Assert.Equal(JJStringComparison.Normalized, fresh.StringComparison);
        Assert.Equal(100_000, fresh.Limits.MaxNodes);
        Assert.Equal(8, fresh.Scoring.MissingWeight);
    }

    [Theory]
    [InlineData(JJStringComparison.Exact, JJNumberComparison.Exact, 70)]
    [InlineData(JJStringComparison.Normalized, JJNumberComparison.Exact, 75)]
    [InlineData(JJStringComparison.Exact, JJNumberComparison.Numeric, 75)]
    [InlineData(JJStringComparison.Normalized, JJNumberComparison.Numeric, 80)]
    [InlineData(JJStringComparison.Fuzzy, JJNumberComparison.Numeric, 85)]
    public void Independent_rules_preserve_the_documented_scores(JJStringComparison strings, JJNumberComparison numbers, double score)
    {
        var settings = new JJMatchSettings { StringComparison = strings, NumberComparison = numbers };
        var result = JJMatcher.Match(Input("Left"), Input("Right"), settings: settings);
        Assert.Equal(score, result.Score);
        Assert.Equal(95, result.Diagnostics.StructuralScore);
        Assert.Equal(result.Score, result.Diagnostics.ValueScore);
    }

    [Fact]
    public void Legacy_and_simple_setters_share_state_without_losing_other_rules()
    {
        var settings = new JJMatchSettings { ValueMode = JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric };
        Assert.Equal(JJStringComparison.Fuzzy, settings.StringComparison);
        Assert.Equal(JJNumberComparison.Numeric, settings.NumberComparison);
        settings.StringComparison = JJStringComparison.Normalized;
        Assert.Equal(JJValueMatchingMode.Normalized | JJValueMatchingMode.Numeric, settings.ValueMode);
        settings.NumberComparison = JJNumberComparison.Exact;
        Assert.Equal(JJValueMatchingMode.Normalized, settings.ValueMode);
        settings.MaxDepth = 64;
        Assert.Equal(64, settings.Limits.MaxDepth);
        settings.Limits.MaxDepth = 128;
        Assert.Equal(128, settings.MaxDepth);
        Assert.Equal(JJArrayComparison.Unordered, settings.ArrayComparison);
        settings.ArrayComparison = JJArrayComparison.DistinctShapes;
        Assert.Equal(JJArrayMatchingMode.StructureOnly, settings.ArrayMode);
    }

    [Fact]
    public void Explicit_legacy_options_override_settings_scope_and_diagnostics_can_be_disabled()
    {
        var settings = JJMatchSettings.StructureOnly;
        var full = JJMatcher.Match(Input("Left"), Input("Right"), JJMatchOptions.All, settings);
        Assert.Equal(70, full.Score);
        settings.Mode = JJComparisonMode.Full;
        settings.IncludeStructuralScore = false;
        var simple = JJMatcher.Match(Input("Left"), Input("Right"), settings: settings);
        Assert.Equal(70, simple.Score);
        Assert.Equal(7, simple.Differences.Count);
        Assert.Null(simple.Diagnostics.StructuralScore);
        Assert.Null(simple.StructuralMatchPercentage);
        Assert.Equal(simple.ElapsedMilliseconds, simple.Diagnostics.ElapsedMilliseconds);
    }

    [Fact]
    public void Minimal_export_includes_scope_and_all_weight_overrides_and_reloads_same_result()
    {
        var settings = JJMatchSettings.StructureOnly;
        settings.Scoring.Mode = JJScoringMode.Weighted;
        settings.Scoring.PropertyNameCaseMismatchWeight = 2;
        settings.Scoring.ValueCaseMismatchWeight = 3;
        settings.Scoring.MissingWeight = 9;
        settings.Scoring.AddedWeight = 4;
        settings.Scoring.TypeMismatchWeight = 7;
        settings.Scoring.ValueMismatchWeight = 5;
        settings.Limits.MaxDepth = 128;
        settings.Limits.MaxInputLength = 200_000;
        settings.Limits.MaxNodes = 20_000;
        settings.Limits.MaxAssignmentSize = 64;
        settings.Limits.MaxWork = 100_000;
        settings.IncludeStructuralScore = false;
        settings.DecodeStringifiedJson = false;
        var loaded = JJMatchSettings.FromJson(settings.ToJson());
        Assert.Equal(JJComparisonMode.StructureOnly, loaded.Mode);
        Assert.Equal(2, loaded.Scoring.PropertyNameCaseMismatchWeight);
        Assert.Equal(3, loaded.Scoring.ValueCaseMismatchWeight);
        Assert.Equal(9, loaded.Scoring.MissingWeight);
        Assert.Equal(4, loaded.Scoring.AddedWeight);
        Assert.Equal(7, loaded.Scoring.TypeMismatchWeight);
        Assert.Equal(5, loaded.Scoring.ValueMismatchWeight);
        Assert.Equal(128, loaded.Limits.MaxDepth);
        Assert.Equal(200_000, loaded.MaxInputLength);
        Assert.Equal(20_000, loaded.MaxNodes);
        Assert.Equal(64, loaded.MaxAssignmentSize);
        Assert.Equal(100_000, loaded.MaxWork);
        Assert.False(loaded.DecodeStringifiedJson);
        Assert.False(loaded.IncludeStructuralScore);
        var result = JJMatcher.Match(Input("Left"), Input("Right"), settings: loaded);
        Assert.Equal(100d * 380 / 393, result.Score);
        Assert.Equal(new[] { JJDifferenceKind.Missing, JJDifferenceKind.Added }, result.Differences.Select(d => d.Kind));
    }

    [Fact]
    public void Export_omits_defaults_and_inactive_values_unless_requested()
    {
        var settings = JJMatchSettings.Default;
        using var minimal = JsonDocument.Parse(settings.ToJson());
        Assert.Empty(minimal.RootElement.EnumerateObject());
        settings.FuzzyThreshold = 0.7;
        settings.Scoring.ValueCaseMismatchWeight = 2;
        var active = JJMatchSettings.FromJson(settings.ToJson());
        Assert.Equal(0.8, active.FuzzyThreshold);
        Assert.Equal(4, active.Scoring.ValueCaseMismatchWeight);
        var complete = JJMatchSettings.FromJson(settings.ToJson(includeInactiveSettings: true));
        Assert.Equal(0.7, complete.FuzzyThreshold);
        Assert.Equal(2, complete.Scoring.ValueCaseMismatchWeight);
        settings.StringComparison = JJStringComparison.Fuzzy;
        settings.NumberComparison = JJNumberComparison.Numeric;
        settings.ArrayComparison = JJArrayComparison.DistinctShapes;
        settings.IgnoredPropertyPrefix = "_";
        var loaded = JJMatchSettings.FromJson(settings.ToJson());
        Assert.Equal(JJStringComparison.Fuzzy, loaded.StringComparison);
        Assert.Equal(JJNumberComparison.Numeric, loaded.NumberComparison);
        Assert.Equal(JJArrayMatchingMode.StructureOnly, loaded.ArrayMode);
        Assert.Equal(0.7, loaded.FuzzyThreshold);
        Assert.Equal("_", loaded.IgnoredPropertyPrefix);
    }

    [Fact]
    public void Stringified_decoding_can_be_disabled_at_root_and_nested_locations()
    {
        var plain = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ComplexJson", "Left.json"));
        var encoded = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Stringified", "LeftStringify.txt"));
        var settings = new JJMatchSettings { DecodeStringifiedJson = false, Limits = { MaxDepth = 128 } };
        var result = JJMatcher.Match(plain, encoded, settings: settings);
        Assert.Equal(0, result.Score);
        Assert.Equal(JJDifferenceKind.TypeMismatch, Assert.Single(result.Differences).Kind);
        var nested = JJMatcher.Match(Input("Left", "nestedStringified"), Input("Right", "nestedStringified"), settings: settings);
        Assert.Contains(nested.Differences, d => d.Path == "$.changed");
        Assert.DoesNotContain(nested.Differences, d => d.Path == "$.changed.status");
    }

    [Fact]
    public void New_settings_are_validated_and_nested_limits_are_enforced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JJMatchSettings { StringComparison = (JJStringComparison)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JJMatchSettings { NumberComparison = (JJNumberComparison)99 });
        Assert.Throws<ArgumentNullException>(() => new JJMatchSettings { Limits = null! });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JJMatchSettings { Mode = (JJComparisonMode)99 }.ToJson());
        var input = Input("Left");
        Assert.Throws<InvalidOperationException>(() => JJMatcher.Match(input, input,
            settings: new JJMatchSettings { Limits = { MaxNodes = 1 } }));
    }
}
