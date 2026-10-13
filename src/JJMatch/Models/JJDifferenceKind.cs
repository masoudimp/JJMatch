namespace JJMatch;

/// <summary>The way two JSON documents differ at one path.</summary>
public enum JJDifferenceKind
{
    /// <summary>The first document has something here that the second does not.</summary>
    Missing,

    /// <summary>The second document has something here that the first does not.</summary>
    Added,

    /// <summary>Both documents have something here, but of different data types.</summary>
    TypeMismatch,

    /// <summary>Both documents have a value of the same data type here, but the values differ.</summary>
    ValueMismatch
}
