namespace JJMatch;

/// <summary>Selects how arrays are compared.</summary>
/// <remarks>
/// <para>
/// Every mode describes how array elements are paired and counted. Strict uses the selected value rules. StructureOnly ignores all values below arrays.
/// </para>
/// <para>
/// <see cref="JJMatchSettings"/> explicitly defaults to <see cref="Strict"/>. The order of an array's elements and of an object's properties is never significant, in any mode.
/// </para>
/// </remarks>
public enum JJArrayMatchingMode
{
    /// <summary>
    /// Compares the structural shapes present in arrays. Element count and primitive values are ignored throughout
    /// each array subtree; nested structural distinctions are preserved. The enum zero value is retained for
    /// compatibility; JJMatchSettings defaults to Strict.
    /// </summary>
    StructureOnly = 0,

    /// <summary>
    /// Compares array elements with their values, wherever they stand in the array, preserving how many times each
    /// occurs: every element pairs with at most one element of the other array.
    /// </summary>
    Strict = 1
}
