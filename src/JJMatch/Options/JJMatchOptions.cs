namespace JJMatch;

/// <summary>Selects comparison features. Omit this argument for structure and value matching.</summary>
/// <remarks>
/// All is zero, a library convention: compare it with equality, not HasFlag. StructuralMatching alone
/// ignores primitive values. ValueMatching alone includes the structural context needed to compare values,
/// but omits the separate structural diagnostic score. Both flags, or neither, compare structure and values.
/// Array and value policies are configured with JJMatchSettings. Differences are always reported.
/// </remarks>
[Flags]
public enum JJMatchOptions
{
    /// <summary>Default structure and value comparison, with differences.</summary>
    All = 0,

    /// <summary>Compare structure, ignoring primitive values unless ValueMatching is also selected.</summary>
    StructuralMatching = 1,

    /// <summary>Compare values in their structural context. Alone, omits StructuralMatchPercentage.</summary>
    ValueMatching = 2
}
