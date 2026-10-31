using System.ComponentModel;

namespace JJMatch;

/// <summary>An immutable, detached comparison result.</summary>
public sealed class JJMatchResult
{
    internal JJMatchResult(double score, IReadOnlyList<JJDifference> differences, double? structuralScore, double? valueScore,
        double elapsedMilliseconds)
    {
        Diagnostics = new JJMatchDiagnostics(structuralScore, valueScore, elapsedMilliseconds);
        Score = score;
        Differences = differences;
    }

    /// <summary>Optional scores and timing, separated from the main verdict and differences.</summary>
    public JJMatchDiagnostics Diagnostics { get; }

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
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public double? StructuralMatchPercentage => Diagnostics.StructuralScore;

    /// <summary>The value-aware document Score, including structural context, or null when value comparison is disabled. Not a separately weighted component.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public double? ValueMatchPercentage => Diagnostics.ValueScore;

    /// <summary>Wall-clock time the call took, in milliseconds, from validation through parsing, comparison and difference reporting.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public double ElapsedMilliseconds => Diagnostics.ElapsedMilliseconds;
}

/// <summary>Read-only diagnostic information. Legacy result properties expose the same values.</summary>
public sealed class JJMatchDiagnostics
{
    internal JJMatchDiagnostics(double? structuralScore, double? valueScore, double elapsedMilliseconds)
    {
        StructuralScore = structuralScore;
        ValueScore = valueScore;
        ElapsedMilliseconds = elapsedMilliseconds;
    }
    /// <summary>Independent structural score, or null when not requested.</summary>
    public double? StructuralScore { get; }
    /// <summary>Full document score when values participate; otherwise null. Retained for compatibility.</summary>
    public double? ValueScore { get; }
    /// <summary>Total comparison wall-clock time in milliseconds.</summary>
    public double ElapsedMilliseconds { get; }
}
