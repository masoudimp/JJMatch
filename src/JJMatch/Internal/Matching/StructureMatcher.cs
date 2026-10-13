using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JJMatch;

/// <summary>Aligns two JSON trees under the selected array and primitive value rules.</summary>
internal static class StructureMatcher
{
    /// <summary>
    /// Returns what matched and every structural difference between <paramref name="left"/> and <paramref name="right"/>.
    /// Primitive values, object property order, array element order and repeated element shapes are ignored.
    /// </summary>
    internal static StructureMatchResult Match(JsonStructureNode left, JsonStructureNode right) =>
        Match(left, right, JJArrayMatchingMode.StructureOnly, JJValueMatchingMode.None, false, null);

    // Structural alignment stays here; primitive value decisions are delegated to ValueMatcher. One cache can serve
    // several calls over the same trees with the same modes, such as the value-aware and structural passes of a
    // public comparison. The name mode decides which property names pair; a cache given here must have been built
    // with the same one. The fuzzy threshold is handed to every value comparison; omitted, it is the library default.
    internal static StructureMatchResult Match(JsonStructureNode left, JsonStructureNode right,
        JJArrayMatchingMode arrayMode, JJValueMatchingMode valueMode, bool compareValues, ComparisonBudget? budget,
        ComparisonCache? cache = null, JJPropertyNameMatchingMode nameMode = JJPropertyNameMatchingMode.Exact,
        double fuzzyThreshold = ValueMatcher.DefaultFuzzyThreshold)
    {
        var rules = new Rules(arrayMode, valueMode, compareValues, budget, cache ?? new ComparisonCache(arrayMode, budget, nameMode), fuzzyThreshold);
        var differences = new List<StructureDifference>();
        var matchedCount = Compare(left, right, new List<string?>(), differences, rules);
        return new StructureMatchResult(matchedCount, differences);
    }

    private sealed class Rules
    {
        internal Rules(JJArrayMatchingMode arrays, JJValueMatchingMode values, bool compareValues, ComparisonBudget? budget, ComparisonCache cache, double fuzzyThreshold)
        {
            Arrays = arrays;
            Values = values;
            CompareValues = compareValues;
            Budget = budget;
            Cache = cache;
            FuzzyThreshold = fuzzyThreshold;
        }
        internal JJArrayMatchingMode Arrays { get; }
        internal JJValueMatchingMode Values { get; }
        internal bool CompareValues { get; }
        internal ComparisonBudget? Budget { get; }
        internal ComparisonCache Cache { get; }
        internal double FuzzyThreshold { get; }
    }

    // The key under which a property name is paired: the name itself under Exact, and under Normalized the name
    // trimmed and in NFC, to be compared ignoring case, exactly as Normalized compares string values.
    internal static string NameKey(string name, JJPropertyNameMatchingMode mode) =>
        mode == JJPropertyNameMatchingMode.Normalized ? ValueMatcher.Normalize(name) : name;

    internal static StringComparer NameComparer(JJPropertyNameMatchingMode mode) =>
        mode == JJPropertyNameMatchingMode.Normalized ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // Denominator for Dice: array shape mode counts one representative of each structural shape, recursively.
    internal static int CountNodes(JsonStructureNode node, JJArrayMatchingMode mode, ComparisonBudget budget, ComparisonCache cache)
    {
        if (mode == JJArrayMatchingMode.StructureOnly)
        {
            return cache.Size(node);
        }

        budget.Spend();
        var count = 1;
        foreach (var child in node.Children) count += CountNodes(child, mode, budget, cache);
        return count;
    }

    // Returns the number of matched nodes. The path holds the property names from the root down to the two nodes,
    // with null for an array level. Nothing below a type mismatch is compared. Without a difference list only the
    // count is computed, which is all a speculative pair of a group needs. Two subtrees the cache knows to be
    // identical are matched whole.
    private static int Compare(
        JsonStructureNode left,
        JsonStructureNode right,
        List<string?> path,
        List<StructureDifference>? differences,
        Rules rules)
    {
        rules.Budget?.Spend();
        if (left.Children.Count != 0 && rules.Cache.TryGetIdentical(left, right, rules.CompareValues, out var size))
        {
            return size;
        }

        if (TypeOf(left) != TypeOf(right))
        {
            differences?.Add(new StructureDifference(StructureMismatchType.TypeMismatch, FormatPath(path), left, right));
            return 0;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                return 1 + CompareProperties(left, right, path, differences, rules);

            case JsonValueKind.Array:
                return 1 + CompareElements(left, right, path, differences, rules);

            default:
                // The cache keeps folded texts for the candidates of a group, which are compared once per candidate;
                // a reported pair is compared once and would only fill it.
                if (rules.CompareValues && !ValueMatcher.MatchValues(left, right, rules.Values, rules.Budget, differences is null ? rules.Cache : null, rules.FuzzyThreshold))
                {
                    differences?.Add(new StructureDifference(StructureMismatchType.ValueMismatch, FormatPath(path), left, right));
                    return 0;
                }
                return 1;
        }
    }

    // True and False are one Boolean type; every other kind is a type of its own.
    internal static JsonValueKind TypeOf(JsonStructureNode node) =>
        node.ValueKind == JsonValueKind.False ? JsonValueKind.True : node.ValueKind;

    // Properties are paired by name in any order, under the name rule, and so are the properties sharing a name:
    // see PairSameName. An unpaired subtree is one difference at its top.
    private static int CompareProperties(
        JsonStructureNode left,
        JsonStructureNode right,
        List<string?> path,
        List<StructureDifference>? differences,
        Rules rules)
    {
        var leftNames = rules.Cache.Names(left);
        var rightNames = rules.Cache.Names(right);

        // The right partner of each left property, with the matched count a group assignment already found.
        var partners = new (int Right, int MatchedCount)[left.Children.Count];
        for (var index = 0; index < partners.Length; index++) partners[index] = (-1, -1);
        foreach (var name in leftNames.Last)
        {
            if (!rightNames.Last.TryGetValue(name.Key, out var rightLast)) continue;
            if (leftNames.Previous[name.Value] < 0 && rightNames.Previous[rightLast] < 0)
            {
                partners[name.Value].Right = rightLast;
                continue;
            }

            path.Add(left.Children[name.Value].PropertyName);
            PairSameName(left, leftNames.SameName(name.Value), right, rightNames.SameName(rightLast), partners, path, rules);
            path.RemoveAt(path.Count - 1);
        }

        var matchedCount = 0;
        var pairedRight = new bool[right.Children.Count];
        for (var index = 0; index < left.Children.Count; index++)
        {
            var property = left.Children[index];
            path.Add(property.PropertyName);
            var partner = partners[index];
            if (partner.Right < 0)
            {
                var mismatchType = rightNames.Contains(property.PropertyName!)
                    ? StructureMismatchType.DuplicatePropertyCountMismatch
                    : StructureMismatchType.MissingProperty;
                differences?.Add(new StructureDifference(mismatchType, FormatPath(path), property, null));
            }
            else
            {
                pairedRight[partner.Right] = true;
                matchedCount += differences is null && partner.MatchedCount >= 0
                    ? partner.MatchedCount
                    : Compare(property, right.Children[partner.Right], path, differences, rules);
            }
            path.RemoveAt(path.Count - 1);
        }

        for (var index = 0; index < right.Children.Count; index++)
        {
            if (!pairedRight[index])
            {
                var property = right.Children[index];
                var mismatchType = leftNames.Contains(property.PropertyName!)
                    ? StructureMismatchType.DuplicatePropertyCountMismatch
                    : StructureMismatchType.ExtraProperty;
                path.Add(property.PropertyName);
                differences?.Add(new StructureDifference(mismatchType, FormatPath(path), null, property));
                path.RemoveAt(path.Count - 1);
            }
        }

        return matchedCount;
    }

    // The properties of an object by name: the index of the last property of each name, and for every property
    // the index of the previous one sharing its name, or -1. Built once per object compared, or once per group
    // member. A name is declared once when its last property has no previous one. Names are keyed under the name
    // rule, so under Normalized "name" and "Name" are one name.
    internal sealed class NameIndex
    {
        private readonly JJPropertyNameMatchingMode _mode;

        internal NameIndex(JsonStructureNode node, JJPropertyNameMatchingMode mode)
        {
            _mode = mode;
            Last = new Dictionary<string, int>(node.Children.Count, NameComparer(mode));
            Previous = new int[node.Children.Count];
            for (var index = 0; index < node.Children.Count; index++)
            {
                var name = NameKey(node.Children[index].PropertyName!, mode);
                Previous[index] = Last.TryGetValue(name, out var previous) ? previous : -1;
                Last[name] = index;
            }
        }

        // Keyed by NameKey; the key kept is the spelling of the first property declared under it.
        internal Dictionary<string, int> Last { get; }
        internal int[] Previous { get; }

        internal bool Contains(string name) => Last.ContainsKey(NameKey(name, _mode));

        // The indices of the properties sharing the name of the one at last, in document order.
        internal List<int> SameName(int last)
        {
            var indices = new List<int>();
            for (var index = last; index >= 0; index = Previous[index]) indices.Add(index);
            indices.Reverse();
            return indices;
        }
    }

    // Pairs the properties of two objects that share one name, whatever their order: see Assign. Every pair may be
    // made, whatever the types. An occurrence left without a partner is a surplus.
    private static void PairSameName(
        JsonStructureNode left,
        List<int> leftIndices,
        JsonStructureNode right,
        List<int> rightIndices,
        (int Right, int MatchedCount)[] partners,
        List<string?> path,
        Rules rules)
    {
        var leftGroup = new List<JsonStructureNode>(leftIndices.Count);
        foreach (var index in leftIndices) leftGroup.Add(left.Children[index]);
        var rightGroup = new List<JsonStructureNode>(rightIndices.Count);
        foreach (var index in rightIndices) rightGroup.Add(right.Children[index]);

        var assigned = Assign(leftGroup, rightGroup, true, path, rules);
        for (var leftIndex = 0; leftIndex < leftIndices.Count; leftIndex++)
        {
            if (assigned[leftIndex].Right >= 0)
            {
                partners[leftIndices[leftIndex]] = (rightIndices[assigned[leftIndex].Right], assigned[leftIndex].MatchedCount);
            }
        }
    }

    // Each side is reduced to its distinct element shapes, which are paired as a group: see Assign. Only those
    // CanPair allows are paired. A pair of different shapes always yields at least one difference; a shape without
    // a partner is reported whole.
    private static int CompareElements(
        JsonStructureNode left,
        JsonStructureNode right,
        List<string?> path,
        List<StructureDifference>? differences,
        Rules rules)
    {
        path.Add(null);
        if (rules.Arrays == JJArrayMatchingMode.Strict)
        {
            var count = 0;
            for (var index = 0; index < Math.Max(left.Children.Count, right.Children.Count); index++)
            {
                rules.Budget?.Spend();
                if (index >= right.Children.Count)
                    differences?.Add(new StructureDifference(StructureMismatchType.MissingArrayElementShape, FormatPath(path), left.Children[index], null));
                else if (index >= left.Children.Count)
                    differences?.Add(new StructureDifference(StructureMismatchType.ExtraArrayElementShape, FormatPath(path), null, right.Children[index]));
                else
                    count += Compare(left.Children[index], right.Children[index], path, differences, rules);
            }
            path.RemoveAt(path.Count - 1);
            return count;
        }

        var shapesOnly = rules.Arrays == JJArrayMatchingMode.StructureOnly;
        // Shape equivalence ignores all values below this array, so collapsing repeated shapes stays well-defined.
        if (shapesOnly && rules.CompareValues)
            rules = new Rules(rules.Arrays, rules.Values, false, rules.Budget, rules.Cache, rules.FuzzyThreshold);
        var leftShapes = shapesOnly ? DistinctShapes(left, rules.Cache) : left.Children.ToList();
        var rightShapes = shapesOnly ? DistinctShapes(right, rules.Cache) : right.Children.ToList();

        var matchedCount = 0;
        // One element on each side is paired when it may be, without identities or a solver.
        if (leftShapes.Count == 1 && rightShapes.Count == 1 && (!shapesOnly || CanPair(leftShapes[0], rightShapes[0], rules)))
        {
            rules.Budget?.Assignment(1, 1);
            matchedCount = Compare(leftShapes[0], rightShapes[0], path, differences, rules);
            path.RemoveAt(path.Count - 1);
            return matchedCount;
        }

        var assigned = Assign(leftShapes, rightShapes, !shapesOnly, path, rules);
        var pairedRight = new HashSet<int>();
        for (var leftIndex = 0; leftIndex < leftShapes.Count; leftIndex++)
        {
            if (assigned[leftIndex].Right >= 0)
            {
                pairedRight.Add(assigned[leftIndex].Right);
                matchedCount += differences is null
                    ? assigned[leftIndex].MatchedCount
                    : Compare(leftShapes[leftIndex], rightShapes[assigned[leftIndex].Right], path, differences, rules);
            }
            else
            {
                differences?.Add(new StructureDifference(StructureMismatchType.MissingArrayElementShape, FormatPath(path), leftShapes[leftIndex], null));
            }
        }

        for (var rightIndex = 0; rightIndex < rightShapes.Count; rightIndex++)
        {
            if (!pairedRight.Contains(rightIndex))
            {
                differences?.Add(new StructureDifference(StructureMismatchType.ExtraArrayElementShape, FormatPath(path), null, rightShapes[rightIndex]));
            }
        }

        path.RemoveAt(path.Count - 1);
        return matchedCount;
    }

    // Pairs the occurrences of a group one to one and returns for each left one the index of its right partner, or
    // -1, with their matched count. Among same-named properties every pair may be made; among array element
    // shapes only those CanPair allows. The pairs are chosen to match the most nodes over the whole group, which is
    // the same as leaving the fewest nodes unmatched: every pair that may be made is counted once and the group is
    // assigned as a whole, whatever its size, so the count is the maximum any pairing could reach. Only counts are
    // computed here; a caller that reports differences compares the pairs taken once more, which costs one
    // comparison per pair instead of one per candidate.
    private static (int Right, int MatchedCount)[] Assign(
        List<JsonStructureNode> left,
        List<JsonStructureNode> right,
        bool pairUnrelated,
        List<string?> path,
        Rules rules)
    {
        var assigned = new (int Right, int MatchedCount)[left.Count];
        for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            assigned[leftIndex].Right = -1;
        }

        if (left.Count == 0 || right.Count == 0)
        {
            return assigned;
        }

        rules.Budget?.Assignment(left.Count, right.Count);
        var leftSizes = new int[left.Count];
        var rightSizes = new int[right.Count];
        for (var leftIndex = 0; leftIndex < left.Count; leftIndex++) leftSizes[leftIndex] = rules.Cache.Size(left[leftIndex], !pairUnrelated);
        for (var rightIndex = 0; rightIndex < right.Count; rightIndex++) rightSizes[rightIndex] = rules.Cache.Size(right[rightIndex], !pairUnrelated);

        var matchedCounts = new int[left.Count, right.Count];
        for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            for (var rightIndex = 0; rightIndex < right.Count; rightIndex++)
            {
                matchedCounts[leftIndex, rightIndex] = pairUnrelated || CanPair(left[leftIndex], right[rightIndex], rules)
                    ? Compare(left[leftIndex], right[rightIndex], path, null, rules)
                    : -1;
            }
        }
        // A pair has no differences exactly when every node of both subtrees is matched.
        var sameShape = new bool[left.Count, right.Count];
        for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            for (var rightIndex = 0; rightIndex < right.Count; rightIndex++)
            {
                sameShape[leftIndex, rightIndex] = matchedCounts[leftIndex, rightIndex] == leftSizes[leftIndex]
                    && matchedCounts[leftIndex, rightIndex] == rightSizes[rightIndex];
            }
        }
        var partners = SolveAssignment(matchedCounts, sameShape, rules.Budget);
        for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            if (partners[leftIndex] >= 0)
            {
                assigned[leftIndex] = (partners[leftIndex], matchedCounts[leftIndex, partners[leftIndex]]);
            }
        }
        return assigned;
    }

    // The assignment that matches the most nodes over the whole group; among those, the one pairing the most
    // occurrences of the same shape, which have no differences; among those, the one making the most pairs. Among
    // equally good assignments, the first left occurrence gets the earliest right partner that any of them gives
    // it, then the second, and so on; having a partner comes before having none.
    // The table is padded to a square with rows and columns that stand for "no partner", and solved once with the
    // Hungarian algorithm, in O(n^3). Its potentials tell which cells can be part of a best assignment (the tight
    // ones), so the tie rule is applied by rearranging within those cells, without solving again: O(n^4) at worst.
    // The solver is checked against an exhaustive search through this entry, which reads the tie rule from each
    // pair's differences.
    private static int[] BestAssignment(int[,] matchedCounts, List<StructureDifference>?[,] pairDifferences)
    {
        var sameShape = new bool[matchedCounts.GetLength(0), matchedCounts.GetLength(1)];
        for (var row = 0; row < sameShape.GetLength(0); row++)
        {
            for (var column = 0; column < sameShape.GetLength(1); column++)
            {
                sameShape[row, column] = pairDifferences[row, column] is { Count: 0 };
            }
        }
        return SolveAssignment(matchedCounts, sameShape, null);
    }

    private static int[] SolveAssignment(int[,] matchedCounts, bool[,] sameShape, ComparisonBudget? budget)
    {
        var leftCount = matchedCounts.GetLength(0);
        var rightCount = matchedCounts.GetLength(1);
        var size = Math.Max(leftCount, rightCount);

        // A pair costs less than no pair, a pair of the same shape less than any number of other pairs, and one more
        // matched node outweighs any number of pairs of either kind. The costs are computed once; a cell that is
        // not a pair costs 0.
        var cost = new long[size, size];
        for (var row = 0; row < leftCount; row++)
        {
            for (var column = 0; column < rightCount; column++)
            {
                var matched = matchedCounts[row, column];
                if (matched < 0) continue;
                cost[row, column] = -(matched * (long)(size + 1) * (size + 1) + (sameShape[row, column] ? size + 1 : 0) + 1);
            }
        }
        bool IsPair(int row, int column) => row < leftCount && column < rightCount && matchedCounts[row, column] >= 0;

        // Rows and columns are numbered from 1 here; 0 is the algorithm's sentinel.
        var rowPotential = new long[size + 1];
        var columnPotential = new long[size + 1];
        var rowOfColumn = new int[size + 1];
        var previousColumn = new int[size + 1];
        var slack = new long[size + 1];
        var visited = new bool[size + 1];
        for (var row = 1; row <= size; row++)
        {
            rowOfColumn[0] = row;
            for (var other = 0; other <= size; other++)
            {
                slack[other] = long.MaxValue;
                visited[other] = false;
            }
            var column = 0;
            do
            {
                budget?.Spend(size);
                visited[column] = true;
                var currentRow = rowOfColumn[column];
                var delta = long.MaxValue;
                var nextColumn = 0;
                for (var other = 1; other <= size; other++)
                {
                    if (visited[other])
                    {
                        continue;
                    }

                    var reduced = cost[currentRow - 1, other - 1] - rowPotential[currentRow] - columnPotential[other];
                    if (reduced < slack[other])
                    {
                        slack[other] = reduced;
                        previousColumn[other] = column;
                    }
                    if (slack[other] < delta)
                    {
                        delta = slack[other];
                        nextColumn = other;
                    }
                }

                for (var other = 0; other <= size; other++)
                {
                    if (visited[other])
                    {
                        rowPotential[rowOfColumn[other]] += delta;
                        columnPotential[other] -= delta;
                    }
                    else
                    {
                        slack[other] -= delta;
                    }
                }
                column = nextColumn;
            }
            while (rowOfColumn[column] != 0);

            do
            {
                var previous = previousColumn[column];
                rowOfColumn[column] = rowOfColumn[previous];
                column = previous;
            }
            while (column != 0);
        }

        // From here rows and columns are numbered from 0.
        var columnOf = new int[size];
        var rowOf = new int[size];
        for (var column = 0; column < size; column++)
        {
            rowOf[column] = rowOfColumn[column + 1] - 1;
            columnOf[rowOf[column]] = column;
        }

        bool IsTight(int row, int column) => cost[row, column] - rowPotential[row + 1] - columnPotential[column + 1] == 0;

        // Columns settled for an earlier row.
        var settled = new bool[size];

        // Gives the row another tight column that is not settled, moving later rows out of the way when needed.
        bool TryPlace(int row, bool[] seen)
        {
            budget?.Spend(size);
            for (var column = 0; column < size; column++)
            {
                if (settled[column] || seen[column] || !IsTight(row, column))
                {
                    continue;
                }

                seen[column] = true;
                if (rowOf[column] < 0 || TryPlace(rowOf[column], seen))
                {
                    rowOf[column] = row;
                    columnOf[row] = column;
                    return true;
                }
            }
            return false;
        }

        // Moves the row to the column when the rows after it can still all be placed.
        bool TryTake(int row, int column)
        {
            var current = columnOf[row];
            if (current == column)
            {
                return true;
            }

            var displaced = rowOf[column];
            columnOf[row] = column;
            rowOf[column] = row;
            rowOf[current] = -1;
            settled[column] = true;
            if (TryPlace(displaced, new bool[size]))
            {
                return true;
            }

            columnOf[row] = current;
            rowOf[column] = displaced;
            rowOf[current] = row;
            settled[column] = false;
            return false;
        }

        var partners = new int[leftCount];
        for (var row = 0; row < leftCount; row++)
        {
            // A row that gets no partner settles nothing, so that it never holds a column a later row could pair with.
            partners[row] = -1;
            for (var column = 0; column < rightCount && partners[row] < 0; column++)
            {
                if (!settled[column] && IsPair(row, column) && IsTight(row, column) && TryTake(row, column))
                {
                    settled[column] = true;
                    partners[row] = column;
                }
            }
        }
        return partners;
    }

    // The first element of each distinct shape, in document order.
    private static List<JsonStructureNode> DistinctShapes(JsonStructureNode array, ComparisonCache cache)
    {
        var seen = new HashSet<int>();
        var shapes = new List<JsonStructureNode>();
        foreach (var element in array.Children)
        {
            if (seen.Add(cache.Shape(element)))
            {
                shapes.Add(element);
            }
        }
        return shapes;
    }

    // Two primitives of one type and two arrays can always be paired; two objects only when they share a property
    // name under the name rule, or are both empty. Nothing else is paired, so two nodes of the same shape can
    // always be paired.
    private static bool CanPair(JsonStructureNode left, JsonStructureNode right, Rules rules)
    {
        if (TypeOf(left) != TypeOf(right))
        {
            return false;
        }

        if (left.ValueKind != JsonValueKind.Object || (left.Children.Count == 0 && right.Children.Count == 0)) return true;
        // Charge and poll while scanning names; a quadratic Any/Any scan could evade the public work budget.
        var names = rules.Cache.Names(left);
        foreach (var property in right.Children)
        {
            rules.Budget?.Spend(1L + property.PropertyName!.Length);
            if (names.Contains(property.PropertyName!)) return true;
        }
        return false;
    }

    // A name made of letters, digits and underscores that does not start with a digit is written ".name"; any other
    // name, including the empty one, is written "['name']" with backslashes and single quotes escaped.
    internal static string PropertyPath(string name) => FormatPath(new List<string?> { name }).Substring(1);

    private static string FormatPath(List<string?> path)
    {
        var text = new StringBuilder("$");
        foreach (var name in path)
        {
            if (name is null)
            {
                text.Append("[*]");
            }
            else if (IsPlainName(name))
            {
                text.Append('.').Append(name);
            }
            else
            {
                AppendQuotedName(text, name);
            }
        }
        return text.ToString();
    }

    // Letters, digits and underscores, not starting with a digit. Characters are judged as code points, so a letter
    // outside the Basic Multilingual Plane is plain too. Nothing in a plain name needs escaping.
    private static bool IsPlainName(string name)
    {
        if (name.Length == 0 || char.IsDigit(name, 0))
        {
            return false;
        }

        for (var index = 0; index < name.Length; index += char.IsSurrogatePair(name, index) ? 2 : 1)
        {
            if (name[index] != '_' && !char.IsLetterOrDigit(name, index))
            {
                return false;
            }
        }
        return true;
    }

    // Writes ['name']. The backslash and the quote are escaped by a backslash, so that a name cannot end the brackets
    // early, and every character that would not show in the text is written as a JSON escape, so that a path is one
    // printable line and two names that would look alike stay distinct.
    private static void AppendQuotedName(StringBuilder text, string name)
    {
        text.Append("['");
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            switch (character)
            {
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\'':
                    text.Append("\\'");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    var length = char.IsSurrogatePair(name, index) ? 2 : 1;
                    if (IsInvisible(name, index))
                    {
                        for (var unit = index; unit < index + length; unit++)
                        {
                            text.Append("\\u").Append(((int)name[unit]).ToString("X4", CultureInfo.InvariantCulture));
                        }
                    }
                    else
                    {
                        text.Append(name, index, length);
                    }
                    index += length - 1;
                    break;
            }
        }
        text.Append("']");
    }

    // Control and format characters, line and paragraph separators, every space but U+0020, and a lone surrogate.
    private static bool IsInvisible(string name, int index)
    {
        switch (CharUnicodeInfo.GetUnicodeCategory(name, index))
        {
            case UnicodeCategory.Control:
            case UnicodeCategory.Format:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
            case UnicodeCategory.Surrogate:
                return true;
            case UnicodeCategory.SpaceSeparator:
                return name[index] != ' ';
            default:
                return false;
        }
    }
}
