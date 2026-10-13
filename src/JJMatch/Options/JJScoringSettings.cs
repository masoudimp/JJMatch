namespace JJMatch;

/// <summary>
/// How the score is computed: the <see cref="Mode"/> and, for <see cref="JJScoringMode.Weighted"/>, the importance of
/// each <see cref="JJDifferenceKind"/> from <see cref="MinWeight"/> (least important) to <see cref="MaxWeight"/> (most
/// critical). Held by <see cref="JJMatchSettings.Scoring"/>, which is never null; the defaults select Standard scoring.
/// </summary>
/// <remarks>
/// <para>
/// Under <see cref="JJScoringMode.Weighted"/> every matched node weighs <see cref="MaxWeight"/> on each side, and every
/// unmatched node weighs the weight of the difference reported above it: a missing or added subtree weighs its node
/// count times the Missing or Added weight, a type mismatch weighs the nodes of both subtrees, a value mismatch
/// weighs the two primitives, a property whose name only differs weighs the nodes of both properties at
/// <see cref="PropertyNameCaseMismatchWeight"/> instead of a full Missing and a full Added, and a string value that
/// only differs in case, outer white space or Unicode form weighs the two primitives at
/// <see cref="ValueCaseMismatchWeight"/> instead of the full <see cref="ValueMismatchWeight"/>. Each unmatched node
/// lies under exactly one difference, so nothing is counted twice. The score is
/// <c>100 × 2 × MaxWeight × matched / (2 × MaxWeight × matched + weighted unmatched nodes)</c>.
/// </para>
/// <para>
/// A weight of <see cref="MaxWeight"/> makes a lost node count exactly as it does under Standard scoring, and a lower
/// weight counts it proportionally less, so a Weighted score is never below the Standard one. The weights are ignored
/// under <see cref="JJScoringMode.Standard"/>. They never change which nodes pair or which differences are reported.
/// </para>
/// </remarks>
public sealed class JJScoringSettings
{
    /// <summary>The least importance a kind of difference can be given.</summary>
    public const int MinWeight = 1;

    /// <summary>The greatest importance a kind of difference can be given, and the weight of every matched node under Weighted scoring.</summary>
    public const int MaxWeight = 10;

    /// <summary>How the score is computed. Defaults to <see cref="JJScoringMode.Standard"/>, the unweighted Sørensen–Dice score.</summary>
    public JJScoringMode Mode { get; set; } = JJScoringMode.Standard;

    /// <summary>Weight of a node present on the left only (<see cref="JJDifferenceKind.Missing"/>), from 1 to 10. Defaults to 8: expected content is absent.</summary>
    public int MissingWeight { get; set; } = 8;

    /// <summary>Weight of a node present on the right only (<see cref="JJDifferenceKind.Added"/>), from 1 to 10. Defaults to 3: everything expected is still there.</summary>
    public int AddedWeight { get; set; } = 3;

    /// <summary>Weight of a node under a <see cref="JJDifferenceKind.TypeMismatch"/>, on either side, from 1 to 10. Defaults to 10: the shape a consumer relies on is broken.</summary>
    public int TypeMismatchWeight { get; set; } = 10;

    /// <summary>Weight of each of the two primitives of a <see cref="JJDifferenceKind.ValueMismatch"/>, from 1 to 10. Defaults to 6: the shape holds, the content does not.</summary>
    public int ValueMismatchWeight { get; set; } = 6;

    /// <summary>Weight of each of the two properties of a <see cref="JJDifferenceKind.PropertyNameCaseMismatch"/>, from 1 to 10. Defaults to 4: the content is still there, only its name's spelling differs.</summary>
    public int PropertyNameCaseMismatchWeight { get; set; } = 4;

    /// <summary>Weight of each of the two primitives of a <see cref="JJDifferenceKind.ValueCaseMismatch"/>, from 1 to 10. Defaults to 4: the content is still there, only its spelling's case, outer white space or Unicode form differs.</summary>
    public int ValueCaseMismatchWeight { get; set; } = 4;

    internal int Weight(JJDifferenceKind kind) => kind switch
    {
        JJDifferenceKind.Missing => MissingWeight,
        JJDifferenceKind.Added => AddedWeight,
        JJDifferenceKind.TypeMismatch => TypeMismatchWeight,
        JJDifferenceKind.PropertyNameCaseMismatch => PropertyNameCaseMismatchWeight,
        JJDifferenceKind.ValueCaseMismatch => ValueCaseMismatchWeight,
        _ => ValueMismatchWeight
    };

    // The weight the score applies to a difference of the kind: under Standard every difference counts fully.
    internal int AppliedWeight(JJDifferenceKind kind) => Mode == JJScoringMode.Weighted ? Weight(kind) : MaxWeight;

    internal JJScoringSettings Snapshot()
    {
        var copy = (JJScoringSettings)MemberwiseClone();
        if (copy.Mode < JJScoringMode.Standard || copy.Mode > JJScoringMode.Weighted)
            throw new ArgumentOutOfRangeException(nameof(Mode));
        if (copy.MissingWeight < MinWeight || copy.MissingWeight > MaxWeight) throw new ArgumentOutOfRangeException(nameof(MissingWeight));
        if (copy.AddedWeight < MinWeight || copy.AddedWeight > MaxWeight) throw new ArgumentOutOfRangeException(nameof(AddedWeight));
        if (copy.TypeMismatchWeight < MinWeight || copy.TypeMismatchWeight > MaxWeight) throw new ArgumentOutOfRangeException(nameof(TypeMismatchWeight));
        if (copy.ValueMismatchWeight < MinWeight || copy.ValueMismatchWeight > MaxWeight) throw new ArgumentOutOfRangeException(nameof(ValueMismatchWeight));
        if (copy.PropertyNameCaseMismatchWeight < MinWeight || copy.PropertyNameCaseMismatchWeight > MaxWeight) throw new ArgumentOutOfRangeException(nameof(PropertyNameCaseMismatchWeight));
        if (copy.ValueCaseMismatchWeight < MinWeight || copy.ValueCaseMismatchWeight > MaxWeight) throw new ArgumentOutOfRangeException(nameof(ValueCaseMismatchWeight));
        return copy;
    }
}
