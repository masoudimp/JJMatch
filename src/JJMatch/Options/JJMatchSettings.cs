namespace JJMatch;

/// <summary>Optional comparison rules and resource limits. Configure before calling <see cref="JJMatcher.Match"/>.</summary>
public sealed class JJMatchSettings
{
    /// <summary>Array policy. The public API defaults to ordered, count-preserving comparison.</summary>
    public JJArrayMatchingMode ArrayMode { get; set; } = JJArrayMatchingMode.Strict;

    /// <summary>Primitive value rules, combined by OR. Defaults to Exact.</summary>
    public JJValueMatchingMode ValueMode { get; set; } = JJValueMatchingMode.Exact;

    /// <summary>Decode one outer JSON string as another JSON document. Nested string values remain literal. Defaults to false.</summary>
    public bool ParseStringifiedJson { get; set; }

    /// <summary>Maximum container depth of each parsed document, from 1 to 128. Defaults to 64.</summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>Maximum UTF-16 length of each input, including any decoded document. Defaults to 1,000,000.</summary>
    public int MaxInputLength { get; set; } = 1_000_000;

    /// <summary>Maximum total JSON nodes across both inputs, from 1 to 1,000,000. Defaults to 100,000.</summary>
    public int MaxNodes { get; set; } = 100_000;

    /// <summary>Maximum occurrences per side of an unordered or duplicate-property assignment, from 1 to 256. Defaults to 128.</summary>
    public int MaxAssignmentSize { get; set; } = 128;

    /// <summary>Comparison work budget, including speculative pairs, signatures and value-distance work. Defaults to 10,000,000. Not a time limit.</summary>
    public long MaxWork { get; set; } = 10_000_000;

    internal JJMatchSettings Snapshot()
    {
        var copy = (JJMatchSettings)MemberwiseClone();
        if (copy.ArrayMode < JJArrayMatchingMode.StructureOnly || copy.ArrayMode > JJArrayMatchingMode.Strict)
            throw new ArgumentOutOfRangeException(nameof(ArrayMode));
        const JJValueMatchingMode known = JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized
            | JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric;
        if ((copy.ValueMode & ~known) != 0) throw new ArgumentOutOfRangeException(nameof(ValueMode));
        if (copy.MaxDepth < 1 || copy.MaxDepth > 128) throw new ArgumentOutOfRangeException(nameof(MaxDepth));
        if (copy.MaxInputLength < 1) throw new ArgumentOutOfRangeException(nameof(MaxInputLength));
        if (copy.MaxNodes < 1 || copy.MaxNodes > 1_000_000) throw new ArgumentOutOfRangeException(nameof(MaxNodes));
        if (copy.MaxAssignmentSize < 1 || copy.MaxAssignmentSize > 256) throw new ArgumentOutOfRangeException(nameof(MaxAssignmentSize));
        if (copy.MaxWork < 1) throw new ArgumentOutOfRangeException(nameof(MaxWork));
        return copy;
    }
}
