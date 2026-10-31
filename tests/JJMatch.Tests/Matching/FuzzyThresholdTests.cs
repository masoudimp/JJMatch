using System.Text.Json;

namespace JJMatch.Tests.Matching;

public class FuzzyThresholdTests
{
    [Theory]
    [InlineData(25, 7, 0.28)]
    [InlineData(25, 14, 0.56)]
    [InlineData(100, 55, 0.55)]
    public void Decimal_boundary_accepts_the_exact_similarity(int length, int unchanged, double threshold)
    {
        var left = new string('a', length);
        var right = new string('a', unchanged) + new string('b', length - unchanged);
        Assert.True(Matches(left, right, threshold));
        Assert.True(Matches(right, left, threshold));
        Assert.False(Matches(left, right, Math.BitIncrement(threshold)));
    }

    [Fact]
    public void Every_boundary_and_its_adjacent_doubles_obey_the_minimum_similarity()
    {
        for (var length = 2; length <= 64; length++)
        for (var unchanged = 1; unchanged < length; unchanged++)
        {
            var left = new string('a', length);
            var right = new string('a', unchanged) + new string('b', length - unchanged);
            var similarity = (double)unchanged / length;
            Assert.True(Matches(left, right, Math.BitDecrement(similarity)), $"Below {unchanged}/{length}");
            Assert.True(Matches(left, right, similarity), $"At {unchanged}/{length}");
            Assert.False(Matches(left, right, Math.BitIncrement(similarity)), $"Above {unchanged}/{length}");
        }
    }

    [Fact]
    public void Changing_and_serializing_the_same_settings_updates_array_matching()
    {
        var settings = new JJMatchSettings { StringComparison = JJStringComparison.Fuzzy };
        var left = JsonSerializer.Serialize(new[] { new string('a', 25), "unchanged" });
        var right = JsonSerializer.Serialize(new[] { "unchanged", new string('a', 14) + new string('b', 11) });
        foreach (var threshold in new[] { 1.0, 0.56, 0.0, 0.8, 0.56 })
        {
            settings.FuzzyThreshold = threshold;
            var expected = threshold <= 0.56;
            Assert.Equal(expected, JJMatcher.Match(left, right, settings: settings).IsMatch);
            Assert.Equal(expected, JJMatcher.Match(left, right, settings: JJMatchSettings.FromJson(settings.ToJson())).IsMatch);
        }
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_thresholds_are_rejected(double threshold)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Matches("a", "b", threshold));
        Assert.Equal(nameof(JJMatchSettings.FuzzyThreshold), error.ParamName);
    }

    [Fact]
    public void Endpoints_preserve_normalization_and_empty_string_behavior()
    {
        Assert.True(Matches("", "", 1));
        Assert.True(Matches("", "different", 0));
        Assert.False(Matches("", "different", double.Epsilon));
        Assert.True(Matches("  CAFÉ  ", "cafe\u0301", 1));
        Assert.False(Matches("John", "Jon", 1));
    }

    private static bool Matches(string left, string right, double threshold) =>
        JJMatcher.Match(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), settings: new JJMatchSettings
        {
            StringComparison = JJStringComparison.Fuzzy,
            FuzzyThreshold = threshold
        }).IsMatch;
}
