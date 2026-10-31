namespace JJMatch;

/// <summary>Whether primitive values participate in comparison.</summary>
public enum JJComparisonMode
{
    /// <summary>Compare structure and primitive values.</summary>
    Full,
    /// <summary>Compare structure, ignoring primitive values.</summary>
    StructureOnly
}

/// <summary>Rules for comparing string values. Fuzzy includes normalization.</summary>
public enum JJStringComparison
{
    /// <summary>Compare decoded text exactly.</summary>
    Exact,
    /// <summary>Ignore case, outer whitespace and Unicode normalization differences.</summary>
    Normalized,
    /// <summary>Also accept typos within FuzzyThreshold.</summary>
    Fuzzy
}

/// <summary>Rules for comparing JSON numbers, without converting strings to numbers.</summary>
public enum JJNumberComparison
{
    /// <summary>Compare the written number tokens exactly.</summary>
    Exact,
    /// <summary>Compare exact quantities, so 1 and 1.0 match.</summary>
    Numeric
}

/// <summary>Order-independent array comparison policy.</summary>
public enum JJArrayComparison
{
    /// <summary>Compare values while preserving the number of occurrences.</summary>
    Unordered,
    /// <summary>Compare distinct structural shapes, ignoring values and repeated shapes throughout array subtrees.</summary>
    DistinctShapes
}
