namespace JJMatch;

/// <summary>Selects how <see cref="JJMatchResult.Score"/> is computed from the matched and unmatched nodes.</summary>
/// <remarks>
/// The mode changes the score only. Which nodes pair, <see cref="JJMatchResult.IsMatch"/> and
/// <see cref="JJMatchResult.Differences"/> are the same under every mode, and a complete match is 100 under every mode.
/// </remarks>
public enum JJScoringMode
{
    /// <summary>Sørensen–Dice over node counts, every node weighing the same: <c>200 × matched / (left + right)</c>. The default.</summary>
    Standard = 0,

    /// <summary>
    /// Weighted Sørensen–Dice: a matched node weighs <see cref="JJScoringSettings.MaxWeight"/> on each side and an
    /// unmatched node weighs the <see cref="JJScoringSettings"/> weight of the kind of difference reported above it,
    /// so less important differences lower the score less. With every weight at the maximum the score is the Standard one.
    /// </summary>
    Weighted = 1
}
