using System.Text.Json;

namespace JJMatch;

/// <summary>One JSON value in the tree built by <see cref="JsonStructureTraverser.Traverse"/>: its kind, its primitive value and its children.</summary>
internal sealed class JsonStructureNode
{
    internal JsonStructureNode(
        string? propertyName,
        JsonValueKind valueKind,
        string? value,
        IReadOnlyList<JsonStructureNode> children)
    {
        PropertyName = propertyName;
        ValueKind = valueKind;
        Value = value;
        Children = children;
    }

    /// <summary>The decoded property name, or <c>null</c> for the root and for array elements. An empty name is <c>""</c>, never <c>null</c>.</summary>
    public string? PropertyName { get; }

    /// <summary>The JSON kind exactly as parsed; <c>True</c> and <c>False</c> stay distinct.</summary>
    public JsonValueKind ValueKind { get; }

    /// <summary>The decoded text of a string or the raw text of a number; <c>null</c> for every other kind and for a string that cannot be decoded.</summary>
    public string? Value { get; }

    /// <summary>Object properties or array elements in document order; empty for primitives and empty containers.</summary>
    public IReadOnlyList<JsonStructureNode> Children { get; }
}
