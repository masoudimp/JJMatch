namespace JJMatch;

/// <summary>The outcome of <see cref="StructureMatcher.Match(JsonStructureNode, JsonStructureNode)"/>: what matched and every structural difference.</summary>
internal sealed class StructureMatchResult
{
    internal StructureMatchResult(int matchedCount, IReadOnlyList<StructureDifference> differences)
    {
        MatchedCount = matchedCount;
        Differences = differences;
    }

    /// <summary><c>true</c> only when the two structures are identical, that is when there are no differences.</summary>
    public bool IsMatch => Differences.Count == 0;

    /// <summary>
    /// The number of matched nodes under the selected rules, containers included.
    /// In StructureOnly arrays each distinct element shape counts once. Primitive value rules, when enabled, must accept a leaf for it to count.
    /// It is the greatest count that any pairing allowed by the matching rules could reach.
    /// </summary>
    public int MatchedCount { get; }

    /// <summary>Every structural difference, in left document order within each object and array; empty for a full match.</summary>
    public IReadOnlyList<StructureDifference> Differences { get; }
}
