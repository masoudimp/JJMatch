namespace JJMatch.Tests.Matching;

public class WeightedAssignmentPipelineTests
{
    [Fact]
    public void Every_assignment_priority_agrees_with_independent_exhaustive_search()
    {
        // Exercise the actual solver, including the weighted level omitted by BestAssignment's legacy oracle.
        // The reference enumerates partial injections and compares objective tuples, never Hungarian costs.
        var solve = typeof(StructureMatcher).GetMethod("SolveAssignment",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var random = new Random(20261101);
        for (var sample = 0; sample < 5000; sample++)
        {
            var rows = random.Next(1, 6);
            var columns = random.Next(1, 6);
            var matched = new int[rows, columns];
            var same = new bool[rows, columns];
            var levels = new long[,]?[] { new long[rows, columns], new long[rows, columns],
                new long[rows, columns], sample % 2 == 0 ? new long[rows, columns] : null };
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                matched[row, column] = random.Next(-1, 4);
                same[row, column] = matched[row, column] > 0 && random.Next(4) == 0;
                var differences = same[row, column] ? 0 : random.Next(1, 5);
                levels[0]![row, column] = differences;
                var types = random.Next(differences + 1);
                levels[1]![row, column] = types;
                levels[2]![row, column] = -random.Next(differences - types + 1);
                // Signed cost includes the savings from replacing missing/added subtrees.
                if (levels[3] is { } weighted) weighted[row, column] = random.Next(-40, 41);
            }
            Check(matched, same, levels);
        }

        // Force every priority to decide a tie, with each later priority strongly favoring the losing choice.
        // Two rows/columns admit only the diagonal and cross full assignments at the pair-count optimum.
        for (var priority = 0; priority < 4; priority++)
        {
            var matched = new int[,] { { 1, 1 }, { 1, 1 } };
            var same = new bool[2, 2];
            var levels = new long[,]?[4];
            for (var level = 0; level < 4; level++)
                levels[level] = level < priority ? new long[2, 2]
                    : level == priority ? new long[,] { { 1, 0 }, { 0, 1 } }
                    : new long[,] { { -100, 100 }, { 100, -100 } };
            Check(matched, same, levels);
        }

        void Check(int[,] matched, bool[,] same, long[,]?[] levels)
        {
            var rows = matched.GetLength(0);
            var columns = matched.GetLength(1);
            var used = new bool[columns];
            var candidate = new int[rows];
            int[]? expected = null;
            long[]? best = null;
            Search(0, new long[7]);
            var actual = (int[])solve.Invoke(null, [matched, same, levels, null])!;
            Assert.Equal(expected, actual);

            void Search(int row, long[] objective)
            {
                if (row == rows)
                {
                    // Enumeration is document order, with unmatched last; keep the first exact tie.
                    if (best is null || Better(objective, best))
                    {
                        best = (long[])objective.Clone();
                        expected = (int[])candidate.Clone();
                    }
                    return;
                }
                for (var column = 0; column < columns; column++)
                {
                    if (used[column] || matched[row, column] < 0) continue;
                    used[column] = true;
                    candidate[row] = column;
                    var next = (long[])objective.Clone();
                    next[0] += matched[row, column];
                    next[1] += same[row, column] ? 1 : 0;
                    next[2]++;
                    for (var level = 0; level < 4; level++)
                        next[level + 3] -= levels[level]?[row, column] ?? 0;
                    Search(row + 1, next);
                    used[column] = false;
                }
                candidate[row] = -1;
                Search(row + 1, objective);
            }
        }

        static bool Better(long[] candidate, long[] best)
        {
            for (var index = 0; index < candidate.Length; index++)
                if (candidate[index] != best[index]) return candidate[index] > best[index];
            return false;
        }
    }

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
