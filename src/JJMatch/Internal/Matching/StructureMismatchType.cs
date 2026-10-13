namespace JJMatch;

/// <summary>The category of one structural difference found by <see cref="StructureMatcher.Match(JsonStructureNode, JsonStructureNode)"/>.</summary>
internal enum StructureMismatchType
{
    /// <summary>The left object has a property that the right object does not have under any occurrence.</summary>
    MissingProperty,

    /// <summary>The right object has a property that the left object does not have under any occurrence.</summary>
    ExtraProperty,

    /// <summary>Both sides have a value here, of different JSON types.</summary>
    TypeMismatch,

    /// <summary>The left array holds an element shape that the right array does not hold.</summary>
    MissingArrayElementShape,

    /// <summary>The right array holds an element shape that the left array does not hold.</summary>
    ExtraArrayElementShape,

    /// <summary>Both objects have the property, and one of them declares it more often; reported once per surplus occurrence.</summary>
    DuplicatePropertyCountMismatch,

    /// <summary>Paired primitives fail the selected value rules.</summary>
    ValueMismatch
}
