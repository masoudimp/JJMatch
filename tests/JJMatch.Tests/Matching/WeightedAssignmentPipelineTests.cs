namespace JJMatch.Tests.Matching;

public class WeightedAssignmentPipelineTests
{
    private static string Input(string side, string scenario)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "TestData", "ComplexJson", side + ".json")),
            new System.Text.Json.JsonDocumentOptions { MaxDepth = 128 });
        return document.RootElement.GetProperty("publicApi").GetProperty("weightedAssignments")
            .GetProperty(scenario).GetRawText();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Weighted_ties_include_the_cost_of_unpaired_subtrees(bool duplicateProperties, bool reverse)
    {
        var larger = Input("Left", duplicateProperties ? "duplicates" : "arrays");
        var smaller = Input("Right", duplicateProperties ? "duplicates" : "arrays");
        var firstPath = duplicateProperties ? "$.p#1" : "$[0]";
        var secondPath = duplicateProperties ? "$.p#2" : "$[1]";
        var settings = new JJMatchSettings
        {
            Scoring = { Mode = JJScoringMode.Weighted, TypeMismatchWeight = 1,
                MissingWeight = reverse ? 3 : 10, AddedWeight = reverse ? 10 : 3 }
        };

        var result = JJMatcher.Match(reverse ? smaller : larger, reverse ? larger : smaller, settings: settings);

        // Both choices match two nodes and report one type mismatch and one surplus.
        // Pairing the larger subtree costs 4 * 1 + 2 * 10 = 24; pairing the smaller costs 2 * 1 + 4 * 10 = 42.
        Assert.False(result.IsMatch);
        Assert.Equal(62.5, result.Score);
        Assert.Equal(result.Score, result.Diagnostics.StructuralScore);
        var mismatch = Assert.Single(result.Differences, d => d.Kind == JJDifferenceKind.TypeMismatch);
        Assert.Equal(firstPath + ".x", reverse ? mismatch.RightPath : mismatch.LeftPath);
        var surplus = Assert.Single(result.Differences, d => d.Kind == (reverse ? JJDifferenceKind.Added : JJDifferenceKind.Missing));
        Assert.Equal(secondPath, reverse ? surplus.RightPath : surplus.LeftPath);
        Assert.Equal("{\"x\":1}", (reverse ? surplus.RightValue : surplus.LeftValue)!.Value.GetRawText());
    }

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void Weighted_case_name_surplus_is_independent_of_property_order(bool reverse, int surplusWeight)
    {
        var settings = new JJMatchSettings
        {
            Scoring = { Mode = JJScoringMode.Weighted, PropertyNameCaseMismatchWeight = 4,
                MissingWeight = reverse ? 3 : surplusWeight, AddedWeight = reverse ? surplusWeight : 3 }
        };
        foreach (var scenario in new[] { "caseNames", "caseNamesReordered" })
        {
            var left = Input("Left", scenario);
            var right = Input("Right", scenario);
            var result = JJMatcher.Match(reverse ? right : left, reverse ? left : right, settings: settings);
            // Only the root matches. Expensive surplus: pair the array, mass 4*4+1*8=24.
            // Cheap surplus: pair the leaf, mass 2*4+3*1=11.
            Assert.Equal(2000d / (surplusWeight == 8 ? 44 : 31), result.Score);
            var renamed = Assert.Single(result.Differences, d => d.Kind == JJDifferenceKind.PropertyNameCaseMismatch);
            Assert.Equal(surplusWeight == 8 ? "$.name" : "$[' name ']", reverse ? renamed.RightPath : renamed.LeftPath);
            var surplus = Assert.Single(result.Differences, d => d.Kind == (reverse ? JJDifferenceKind.Added : JJDifferenceKind.Missing));
            Assert.Equal(surplusWeight == 8 ? "$[' name ']" : "$.name", reverse ? surplus.RightPath : surplus.LeftPath);
        }
    }
}
