namespace JJMatch;

/// <summary>Selects when two property names label the same property.</summary>
/// <remarks>
/// <para>
/// Identical names always pair. That rule is not a mode and cannot be turned off: <see cref="None"/>, the default,
/// accepts identical names only, and <see cref="Normalized"/> accepts further pairs on top of it.
/// </para>
/// <para>
/// Names are structural: the rule decides which properties are paired, in every comparison and under every
/// <see cref="JJMatchOptions"/>, and so affects the structural score as well as the value-aware one. A pair of
/// names the rule accepts is a match like any other; no difference is reported for the spelling.
/// </para>
/// <para>
/// The rule applies to names only. Values are decided by <see cref="JJValueMatchingMode"/>, and nothing is
/// relaxed about JSON types or about the presence or absence of a property.
/// </para>
/// </remarks>
public enum JJPropertyNameMatchingMode
{
    /// <summary>No relaxation: only identical names pair, character by character, so <c>name</c> and <c>Name</c> are two properties. The default.</summary>
    None = 0,

    /// <summary>
    /// Compares names ignoring case and leading and trailing white space, with precomposed and decomposed Unicode
    /// forms treated as one, exactly as <see cref="JJValueMatchingMode.Normalized"/> compares strings, so
    /// <c>Name</c> matches <c>name</c>. Internal white space and punctuation are kept: <c>postalCode</c> does
    /// not match <c>postal_code</c>. Names of one object that become equal are a duplicate-name group.
    /// </summary>
    Normalized = 1
}
