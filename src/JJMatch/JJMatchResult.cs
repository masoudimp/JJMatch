namespace JJMatch;

/// <summary>An immutable, detached comparison result.</summary>
public sealed class JJMatchResult
{
    internal JJMatchResult(double score, IReadOnlyList<JJDifference> differences, double? structuralScore, double? valueScore,
        double elapsedMilliseconds)
    {
        Score = score;
        Differences = differences;
        StructuralMatchPercentage = structuralScore;
        ValueMatchPercentage = valueScore;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>True exactly when no differences exist under the selected rules. Then Score is 100.</summary>
    public bool IsMatch => Differences.Count == 0;

    /// <summary>
    /// Sørensen–Dice node similarity, from 0 to 100, computed as <see cref="JJMatchSettings.Scoring"/> selects: over
    /// node counts by default, or over node weights under <see cref="JJScoringMode.Weighted"/>. A complete match is
    /// exactly 100 under every mode; mismatches are below 100.
    /// </summary>
    public double Score { get; }

    /// <summary>Differences in left document order, followed by additions within each container. Never null.</summary>
    public IReadOnlyList<JJDifference> Differences { get; }

    /// <summary>Independent structure-only Dice score under the selected array policy and scoring mode, or null when structural diagnostics are disabled.</summary>
    public double? StructuralMatchPercentage { get; }

    /// <summary>The value-aware document Score, including structural context, or null when value comparison is disabled. Not a separately weighted component.</summary>
    public double? ValueMatchPercentage { get; }

    /// <summary>Wall-clock time the call took, in milliseconds, from validation through parsing, comparison and difference reporting.</summary>
    public double ElapsedMilliseconds { get; }
}
