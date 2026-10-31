namespace JJMatch;

/// <summary>Resource limits. Exceeding a limit fails comparison rather than returning an approximate result.</summary>
public sealed class JJMatchLimits
{
    /// <summary>Maximum parsed container depth. Defaults to 512; very large values can exhaust the stack.</summary>
    public int MaxDepth { get; set; } = 512;
    /// <summary>Maximum UTF-16 length per input. Defaults to 1,000,000.</summary>
    public int MaxInputLength { get; set; } = 1_000_000;
    /// <summary>Maximum visited nodes, from 1 to 1,000,000. Defaults to 100,000.</summary>
    public int MaxNodes { get; set; } = 100_000;
    /// <summary>Maximum array or duplicate-property assignment size, from 1 to 256. Defaults to 128.</summary>
    public int MaxAssignmentSize { get; set; } = 128;
    /// <summary>Maximum comparison work units, not milliseconds. Defaults to 10,000,000.</summary>
    public long MaxWork { get; set; } = 10_000_000;

    internal JJMatchLimits Snapshot() => (JJMatchLimits)MemberwiseClone();
}
