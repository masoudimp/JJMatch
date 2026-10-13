using System.Text.Json;

namespace JJMatch;

/// <summary>One difference, with the original locations and JSON values on both sides.</summary>
public sealed class JJDifference
{
    internal JJDifference(JJDifferenceKind kind, int scoreWeight, string? leftPath, string? rightPath, JsonElement? leftValue, JsonElement? rightValue)
    {
        Kind = kind;
        ScoreWeight = scoreWeight;
        LeftPath = leftPath;
        RightPath = rightPath;
        LeftValue = leftValue?.Clone();
        RightValue = rightValue?.Clone();
    }

    /// <summary>The type of difference.</summary>
    public JJDifferenceKind Kind { get; }

    /// <summary>
    /// The importance weight the score applied to this difference, from 1 to 10: the <see cref="JJScoringSettings"/>
    /// weight of its <see cref="Kind"/> under <see cref="JJScoringMode.Weighted"/>, and <see cref="JJScoringSettings.MaxWeight"/>
    /// under <see cref="JJScoringMode.Standard"/>, where every difference counts fully. Each unmatched node under the
    /// difference costs this weight against a matched node's 10.
    /// </summary>
    public int ScoreWeight { get; }

    /// <summary>The left path when present, otherwise the right path.</summary>
    public string Path => LeftPath ?? RightPath!;

    /// <summary>Location in the left document, or null when absent. Duplicate names have a one-based #occurrence suffix.</summary>
    public string? LeftPath { get; }

    /// <summary>Location in the right document, or null when absent. Array indices refer to the original input order.</summary>
    public string? RightPath { get; }

    /// <summary>A detached JSON snapshot of the left element, or nullable null when absent. JSON null is a present JsonElement.</summary>
    public JsonElement? LeftValue { get; }

    /// <summary>A detached JSON snapshot of the right element, or nullable null when absent.</summary>
    public JsonElement? RightValue { get; }

    /// <summary>A short explanation using the relevant paths.</summary>
    public string Message => Kind switch
    {
        JJDifferenceKind.Missing => $"Missing on the right: {LeftPath}.",
        JJDifferenceKind.Added => $"Only on the right: {RightPath}.",
        JJDifferenceKind.TypeMismatch => $"Different JSON types at {LeftPath} and {RightPath}.",
        _ => $"Different values at {LeftPath} and {RightPath}."
    };

    /// <summary>Returns the difference category and explanation.</summary>
    public override string ToString() => $"{Kind}: {Message}";
}
