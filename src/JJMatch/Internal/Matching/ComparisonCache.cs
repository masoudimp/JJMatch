using System.Text.Json;

namespace JJMatch;

// Per-call memo shared by CountNodes and every Match of one comparison. It gives each indexed subtree an exact
// identity under the array and name modes: two subtrees have the same identity exactly when Compare finds no
// difference between them, so such a pair is matched whole without being walked. Identities are interned canonical keys,
// never hashes, so equal identities are a proof and unequal ones only mean the pair must be compared. Nothing is
// indexed until a group is assigned or distinct shapes are needed, so a document without groups costs nothing here.
internal sealed class ComparisonCache
{
    private readonly JJArrayMatchingMode _arrays;
    private readonly JJValueMatchingMode _values;
    private readonly JJPropertyNameMatchingMode _nameMode;
    private readonly ComparisonBudget? _budget;
    private readonly Dictionary<JsonStructureNode, Entry> _entries = new Dictionary<JsonStructureNode, Entry>();
    private readonly Dictionary<JsonStructureNode, StructureMatcher.NameIndex> _names = new Dictionary<JsonStructureNode, StructureMatcher.NameIndex>();
    private readonly Dictionary<Key, int> _identities = new Dictionary<Key, int>();
    private readonly Dictionary<string, int> _texts = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _nameTexts;
    private readonly Dictionary<JsonStructureNode, string> _normalized = new Dictionary<JsonStructureNode, string>();
    private readonly Dictionary<JsonStructureNode, string> _folded = new Dictionary<JsonStructureNode, string>();
    private readonly int[] _primitiveShapes = new int[8]; // By JsonValueKind, whose last member is Null = 7.
    private readonly int[]?[] _rows = new int[3][];
    private int _unique;

    internal ComparisonCache(JJArrayMatchingMode arrays, JJValueMatchingMode values, ComparisonBudget? budget,
        JJPropertyNameMatchingMode nameMode = JJPropertyNameMatchingMode.Exact)
    {
        _arrays = arrays;
        _values = values;
        _nameMode = nameMode;
        _budget = budget;
        _nameTexts = new Dictionary<string, int>(StructureMatcher.NameComparer(nameMode));
    }

    // Shape ignores primitive values; Valued includes those a selected value rule accepts as identical to
    // themselves. Size is the effective node count: every node, or in StructureOnly arrays one per distinct shape.
    // Only containers are kept: a primitive is cheap to compare and to describe again.
    private struct Entry
    {
        internal int Shape;
        internal int Valued;
        internal int Size;
    }

    // The structural identity of an array element, indexing it on first use.
    internal int Shape(JsonStructureNode element) => Index(element, _arrays == JJArrayMatchingMode.StructureOnly).Shape;

    // The effective node count of a subtree, indexing it on first use. An element of a StructureOnly array is only
    // ever compared structurally, so no valued identity is built below it.
    internal int Size(JsonStructureNode node, bool elementOfShapeArray = false) =>
        Index(node, elementOfShapeArray && _arrays == JJArrayMatchingMode.StructureOnly).Size;

    // Whether two already indexed subtrees are identical under the view, and then their common size. A subtree
    // that is not indexed is not indexed here: only group members and their descendants ever are. Primitives are
    // never indexed: a leaf, like an empty container, is as cheap to compare as to look up.
    internal bool TryGetIdentical(JsonStructureNode left, JsonStructureNode right, bool withValues, out int size)
    {
        size = 0;
        if (!_entries.TryGetValue(left, out var leftEntry) || !_entries.TryGetValue(right, out var rightEntry))
        {
            return false;
        }

        size = leftEntry.Size;
        return withValues ? leftEntry.Valued == rightEntry.Valued : leftEntry.Shape == rightEntry.Shape;
    }

    // The property index of an object, kept for an indexed node, which is compared once per candidate of its
    // group, and built afresh for any other.
    internal StructureMatcher.NameIndex Names(JsonStructureNode node)
    {
        if (_names.TryGetValue(node, out var names))
        {
            return names;
        }
        names = new StructureMatcher.NameIndex(node, _nameMode);
        if (_entries.ContainsKey(node)) _names.Add(node, names);
        return names;
    }

    // The normalized and the folded text of a string value, kept across the candidates of a group.
    internal string Normalized(JsonStructureNode node)
    {
        if (!_normalized.TryGetValue(node, out var text))
        {
            _normalized.Add(node, text = ValueMatcher.Normalize(node.Value!));
        }
        return text;
    }

    internal string Folded(JsonStructureNode node)
    {
        if (!_folded.TryGetValue(node, out var text))
        {
            _folded.Add(node, text = Normalized(node).ToUpperInvariant());
        }
        return text;
    }

    // Three rows of the edit distance table, at least as long as asked, reused across the pairs of a call.
    internal int[]?[] Rows(int length)
    {
        if (_rows[0] is null || _rows[0]!.Length < length)
        {
            for (var row = 0; row < 3; row++) _rows[row] = new int[Math.Max(length, 64)];
        }
        return _rows;
    }

    // Below a StructureOnly array values play no part, so there the valued identity is the shape; such a node is
    // only ever compared structurally, so its valued identity is never read. A primitive is described each time it
    // is asked for and never stored; one that no rule accepts as identical to itself gets a fresh identity.
    private Entry Index(JsonStructureNode node, bool shapesOnly)
    {
        if (_entries.TryGetValue(node, out var entry))
        {
            return entry;
        }

        _budget?.Spend(1L + (node.PropertyName?.Length ?? 0));
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                entry = IndexObject(node, shapesOnly);
                break;
            case JsonValueKind.Array:
                entry = IndexArray(node, shapesOnly || _arrays == JJArrayMatchingMode.StructureOnly);
                break;
            default:
                var type = (int)StructureMatcher.TypeOf(node);
                if (_primitiveShapes[type] == 0) _primitiveShapes[type] = 1 + Intern(new[] { PrimitiveShape, type });
                entry.Size = 1;
                entry.Shape = _primitiveShapes[type] - 1;
                entry.Valued = shapesOnly ? entry.Shape
                    : SelfMatching(node) ? Intern(new[] { PrimitiveValue, (int)node.ValueKind, node.Value is null ? -1 : Text(node.Value) })
                    : --_unique;
                return entry;
        }
        _entries.Add(node, entry);
        return entry;
    }

    private const int PrimitiveShape = 1, PrimitiveValue = 2, ObjectShape = 3, ObjectValue = 4, ArrayKey = 5;

    // Properties as (name, identity) pairs sorted by name, then shape, then valued identity, so the order of
    // properties sharing a name plays no part. Names are interned under the name rule, so two objects whose names
    // differ only as that rule allows have one identity. One order serves both keys: equal valued identities have
    // equal shapes, so it is canonical for each.
    private Entry IndexObject(JsonStructureNode node, bool shapesOnly)
    {
        var count = node.Children.Count;
        var children = new (int Name, int Shape, int Valued)[count];
        var size = 1;
        for (var index = 0; index < count; index++)
        {
            var property = node.Children[index];
            var child = Index(property, shapesOnly);
            size += child.Size;
            children[index] = (NameText(property.PropertyName!), child.Shape, child.Valued);
        }

        Array.Sort(children, (a, b) =>
        {
            var order = a.Name.CompareTo(b.Name);
            if (order == 0) order = a.Shape.CompareTo(b.Shape);
            return order != 0 ? order : a.Valued.CompareTo(b.Valued);
        });
        var shape = new int[1 + 2 * count];
        shape[0] = ObjectShape;
        for (var index = 0; index < count; index++)
        {
            shape[1 + 2 * index] = children[index].Name;
            shape[2 + 2 * index] = children[index].Shape;
        }
        var entry = new Entry { Shape = Intern(shape), Size = size };
        if (shapesOnly)
        {
            entry.Valued = entry.Shape;
            return entry;
        }

        var valued = new int[1 + 2 * count];
        valued[0] = ObjectValue;
        for (var index = 0; index < count; index++)
        {
            valued[1 + 2 * index] = children[index].Name;
            valued[2 + 2 * index] = children[index].Valued;
        }
        entry.Valued = Intern(valued);
        return entry;
    }

    // Strict keeps the element order; Unordered sorts the identities, keeping repeats; StructureOnly keeps one of
    // each shape, ignores values below the array, and counts each distinct shape once.
    private Entry IndexArray(JsonStructureNode node, bool shapesOnly)
    {
        var count = node.Children.Count;
        var shapes = new int[1 + count];
        var valued = shapesOnly ? shapes : new int[1 + count];
        var sizes = new int[1 + count];
        shapes[0] = ArrayKey;
        valued[0] = ArrayKey;
        var size = 1;
        for (var index = 0; index < count; index++)
        {
            var child = Index(node.Children[index], shapesOnly);
            shapes[1 + index] = child.Shape;
            valued[1 + index] = child.Valued;
            sizes[1 + index] = child.Size;
            size += child.Size;
        }

        if (_arrays == JJArrayMatchingMode.StructureOnly)
        {
            // Elements of one shape have one size, so the first of each sorted run stands for all of them.
            Array.Sort(shapes, sizes, 1, count);
            size = 1;
            var distinct = 1;
            for (var index = 1; index <= count; index++)
            {
                if (index == 1 || shapes[index] != shapes[index - 1])
                {
                    shapes[distinct++] = shapes[index];
                    size += sizes[index];
                }
            }
            if (distinct < shapes.Length) Array.Resize(ref shapes, distinct);
            var identity = Intern(shapes);
            return new Entry { Shape = identity, Valued = identity, Size = size };
        }

        if (_arrays == JJArrayMatchingMode.Unordered)
        {
            Array.Sort(shapes, 1, count);
            if (!shapesOnly) Array.Sort(valued, 1, count);
        }
        var shapeIdentity = Intern(shapes);
        return new Entry { Shape = shapeIdentity, Valued = shapesOnly ? shapeIdentity : Intern(valued), Size = size };
    }

    // A primitive is identical to itself under the selected value rules when a rule accepts two copies of it: Exact
    // accepts every decodable primitive, Normalized and Fuzzy every decodable string, Numeric every number. A string
    // without a Value matches nothing, itself included.
    private bool SelfMatching(JsonStructureNode node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                return node.Value is not null
                    && (_values & (JJValueMatchingMode.Exact | JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy)) != 0;
            case JsonValueKind.Number:
                return (_values & (JJValueMatchingMode.Exact | JJValueMatchingMode.Numeric)) != 0;
            default:
                return (_values & JJValueMatchingMode.Exact) != 0;
        }
    }

    private int Text(string text)
    {
        if (!_texts.TryGetValue(text, out var identity))
        {
            _texts.Add(text, identity = _texts.Count);
        }
        return identity;
    }

    // Names have their own table, compared under the name rule; values stay ordinal. A key never mixes the two.
    private int NameText(string name)
    {
        var key = StructureMatcher.NameKey(name, _nameMode);
        if (!_nameTexts.TryGetValue(key, out var identity))
        {
            _nameTexts.Add(key, identity = _nameTexts.Count);
        }
        return identity;
    }

    private int Intern(int[] key)
    {
        var wrapped = new Key(key);
        if (!_identities.TryGetValue(wrapped, out var identity))
        {
            _identities.Add(wrapped, identity = _identities.Count);
        }
        return identity;
    }

    // A canonical key compared element by element: a tag, then interned texts and child identities.
    private readonly struct Key : IEquatable<Key>
    {
        private readonly int[] _parts;

        internal Key(int[] parts) => _parts = parts;

        public bool Equals(Key other)
        {
            if (_parts.Length != other._parts.Length) return false;
            for (var index = 0; index < _parts.Length; index++)
            {
                if (_parts[index] != other._parts[index]) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var part in _parts)
            {
                hash = unchecked(hash * 31 + part);
            }
            return hash;
        }
    }
}
