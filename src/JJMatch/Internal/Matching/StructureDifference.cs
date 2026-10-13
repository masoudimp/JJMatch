namespace JJMatch;

/// <summary>One structural difference found by <see cref="StructureMatcher.Match(JsonStructureNode, JsonStructureNode)"/>.</summary>
internal sealed class StructureDifference
{
    internal StructureDifference(
        StructureMismatchType mismatchType,
        string path,
        JsonStructureNode? left,
        JsonStructureNode? right)
    {
        MismatchType = mismatchType;
        Path = path;
        Left = left;
        Right = right;
    }

    /// <summary>The category of the difference.</summary>
    public StructureMismatchType MismatchType { get; }

    /// <summary>Where the difference is, for reporting only: <c>$</c> is the root and <c>[*]</c> is any element of an array.</summary>
    public string Path { get; }

    /// <summary>The left node, with everything below it, or <c>null</c> when only the right side has something here.</summary>
    public JsonStructureNode? Left { get; }

    /// <summary>The right node, with everything below it, or <c>null</c> when only the left side has something here.</summary>
    public JsonStructureNode? Right { get; }
}
