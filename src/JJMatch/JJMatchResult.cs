namespace JJMatch;

/// <summary>An immutable, detached comparison result.</summary>
public sealed class JJMatchResult
{
    internal JJMatchResult(double score, IReadOnlyList<JJDifference> differences, double? structuralScore, double? valueScore)
    {
        Score = score;
        Differences = differences;
        StructuralMatchPercentage = structuralScore;
        ValueMatchPercentage = valueScore;
    }

    /// <summary>True exactly when no differences exist under the selected rules. Then Score is 100.</summary>
    public bool IsMatch => Differences.Count == 0;

    /// <summary>Sørensen–Dice node similarity, from 0 to 100. A complete match is exactly 100; mismatches are below 100.</summary>
    public double Score { get; }

    /// <summary>Differences in left document order, followed by additions within each container. Never null.</summary>
    public IReadOnlyList<JJDifference> Differences { get; }

    /// <summary>Independent structure-only Dice score under the selected array policy, or null when structural diagnostics are disabled.</summary>
    public double? StructuralMatchPercentage { get; }

    /// <summary>The value-aware document Score, including structural context, or null when value comparison is disabled. Not a separately weighted component.</summary>
    public double? ValueMatchPercentage { get; }
}
