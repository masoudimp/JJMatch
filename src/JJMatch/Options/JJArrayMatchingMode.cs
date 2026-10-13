namespace JJMatch;

/// <summary>Selects how arrays are compared.</summary>
/// <remarks>
/// <para>
/// Every mode describes how array elements are paired and counted. Strict and Unordered use the selected value rules. StructureOnly ignores all values below arrays.
/// </para>
/// <para>
/// <see cref="JJMatchSettings"/> explicitly defaults to <see cref="Strict"/>. The order of an object's properties is never significant, in any mode.
/// </para>
/// </remarks>
public enum JJArrayMatchingMode
{
    /// <summary>
    /// Compares the structural shapes present in arrays. Element count, element order and primitive values are
    /// ignored throughout each array subtree; nested structural distinctions are preserved. The enum zero value
    /// is retained for compatibility; JJMatchSettings defaults to Strict.
    /// </summary>
    StructureOnly = 0,

    /// <summary>Compares array elements regardless of their order, while preserving how many times each occurs.</summary>
    Unordered = 1,

    /// <summary>
    /// Compares array elements position by position, preserving both order and count. The only mode in which
    /// array element order matters.
    /// </summary>
    Strict = 2
}
