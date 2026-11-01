namespace JJMatch.Tests.Matching;

public class WeightedAssignmentPipelineTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Weighted_ties_include_the_cost_of_unpaired_subtrees(bool duplicateProperties, bool reverse)
    {
        var larger = duplicateProperties ? "{\"p\":{\"x\":[1,2]},\"p\":{\"x\":1}}" : "[{\"x\":[1,2]},{\"x\":1}]";
        var smaller = duplicateProperties ? "{\"p\":{\"x\":\"a\"}}" : "[{\"x\":\"a\"}]";
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
}
