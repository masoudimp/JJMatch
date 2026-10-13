namespace JJMatch;

/// <summary>Selects when two values are considered equal.</summary>
/// <remarks>
/// <para>
/// Every mode loosens how the values inside two documents are compared. Whether values are compared at all is
/// controlled separately by <see cref="JJMatchOptions.ValueMatching"/>; this enum never scores structure.
/// </para>
/// <para>
/// Identical values always match: decoded strings and written number tokens that are the same, and booleans and
/// null against themselves. That rule is not a mode and cannot be turned off. Every mode is a flag that accepts
/// further pairs on top of it, so modes combine with <c>|</c>, and <see cref="None"/>, the default, accepts
/// identical values only.
/// </para>
/// </remarks>
[Flags]
public enum JJValueMatchingMode
{
    /// <summary>No relaxation: only identical values match. The default.</summary>
    None = 0,

    /// <summary>
    /// Compares strings ignoring case and leading and trailing whitespace, with precomposed and decomposed Unicode
    /// forms treated as one, so <c>"  New York "</c> matches <c>"new york"</c>. Internal whitespace, punctuation and
    /// the JSON type are kept: <c>"New York"</c> does not match <c>"NewYork"</c>, and <c>"123"</c> does not match
    /// <c>123</c>.
    /// </summary>
    Normalized = 1,

    /// <summary>
    /// Compares strings by how similar they are, tolerating typos and small spelling differences, so
    /// <c>"Jon Smith"</c> matches <c>"John Smith"</c>. Case, leading and trailing whitespace and Unicode form are
    /// ignored as under <see cref="Normalized"/>. Optimal string alignment distance permits insertion, deletion,
    /// replacement or adjacent transposition of UTF-16 code units, with no substring edited twice. One edit is
    /// allowed for every five units of the longer folded string, so <c>"Jon"</c> does not match <c>"John"</c>.
    /// Similarity is not transitive. The JSON type is kept: <c>"123"</c> does not match <c>123</c>.
    /// </summary>
    Fuzzy = 2,

    /// <summary>
    /// Compares numbers as quantities instead of as written, exactly and without tolerance, so <c>1</c> matches
    /// <c>1.0</c>, <c>1.5E+10</c> matches <c>15000000000</c> and <c>-0</c> matches <c>0</c>. The JSON type is kept:
    /// <c>"1"</c> does not match <c>1</c>.
    /// </summary>
    Numeric = 4
}
