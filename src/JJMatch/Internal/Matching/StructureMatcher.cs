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
    // Scoring settings are given under Weighted scoring only: their weights then break ties between pairings, see
    // SolveAssignment; they never change which nodes can match.
    internal static StructureMatchResult Match(JsonStructureNode left, JsonStructureNode right,
        JJArrayMatchingMode arrayMode, JJValueMatchingMode valueMode, bool compareValues, ComparisonBudget? budget,
        ComparisonCache? cache = null, JJPropertyNameMatchingMode nameMode = JJPropertyNameMatchingMode.None,
        double fuzzyThreshold = ValueMatcher.DefaultFuzzyThreshold, JJScoringSettings? scoring = null)
    {
        var rules = new Rules(arrayMode, valueMode, compareValues, budget, cache ?? new ComparisonCache(arrayMode, budget, nameMode), fuzzyThreshold, scoring);
        var differences = new List<StructureDifference>();
        var outcome = Compare(left, right, new List<string?>(), differences, rules);
        return new StructureMatchResult(outcome.Matched, differences);
    }

    private sealed class Rules
    {
        internal Rules(JJArrayMatchingMode arrays, JJValueMatchingMode values, bool compareValues, ComparisonBudget? budget, ComparisonCache cache, double fuzzyThreshold, JJScoringSettings? scoring)
        {
            Arrays = arrays;
            Values = values;
            CompareValues = compareValues;
            Budget = budget;
            Cache = cache;
            FuzzyThreshold = fuzzyThreshold;
            Scoring = scoring;
        }
        internal JJArrayMatchingMode Arrays { get; }
        internal JJValueMatchingMode Values { get; }
        internal bool CompareValues { get; }
        internal ComparisonBudget? Budget { get; }
        internal ComparisonCache Cache { get; }
        internal double FuzzyThreshold { get; }
        internal JJScoringSettings? Scoring { get; }
    }

    // What comparing a pair yields: the nodes matched, which the assignment of a group maximizes, and what the
    // differences the pair would report amount to, which only decides among assignments matching equally many
    // nodes: how many there are, how many are type mismatches, how many are case-only (a ValueCaseMismatch or a
    // PropertyNameCaseMismatch), and, when scoring settings are given, the mass the facade's Weighted score charges
    // for them, each difference's weight times the nodes on both sides of it.
    private readonly struct Outcome
    {
        internal Outcome(int matched, int differences = 0, int typeMismatches = 0, int caseOnly = 0, long weighted = 0)
        {
            Matched = matched;
            Differences = differences;
            TypeMismatches = typeMismatches;
            CaseOnly = caseOnly;
            Weighted = weighted;
        }
        internal int Matched { get; }
        internal int Differences { get; }
        internal int TypeMismatches { get; }
        internal int CaseOnly { get; }
        internal long Weighted { get; }
        internal static readonly Outcome Node = new Outcome(1);
        internal static Outcome Of(StructureMismatchType type, long weighted) => new Outcome(0, 1,
            type == StructureMismatchType.TypeMismatch ? 1 : 0,
            type is StructureMismatchType.ValueCaseMismatch or StructureMismatchType.PropertyNameCaseMismatch ? 1 : 0,
            weighted);
        public static Outcome operator +(Outcome left, Outcome right) => new Outcome(left.Matched + right.Matched,
            left.Differences + right.Differences, left.TypeMismatches + right.TypeMismatches,
            left.CaseOnly + right.CaseOnly, left.Weighted + right.Weighted);
    }

    // Reports one difference when a list is given, and in every case returns what it costs a pairing. The path is
    // formatted only for a reported difference, so a speculative comparison pays nothing for it.
    private static Outcome Difference(List<StructureDifference>? differences, Rules rules, StructureMismatchType type,
        List<string?> path, JsonStructureNode? left, JsonStructureNode? right)
    {
        differences?.Add(new StructureDifference(type, FormatPath(path), left, right));
        long weighted = 0;
        if (rules.Scoring is { } scoring)
        {
            var nodes = (left is null ? 0L : rules.Cache.Size(left)) + (right is null ? 0L : rules.Cache.Size(right));
            weighted = scoring.Weight(JJMatcher.Kind(type, left is null, right is null)) * nodes;
        }
        return Outcome.Of(type, weighted);
    }

    // The key under which a property name is paired: the name itself under None, and under Normalized the name
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

    // Returns the outcome of the pair, its matched nodes first of all. The path holds the property names from the
    // root down to the two nodes, with null for an array level. Nothing below a type mismatch is compared. Without
    // a difference list no difference is built or located, which is all a speculative pair of a group needs. Two
    // subtrees the cache knows to be identical are matched whole.
    private static Outcome Compare(
        JsonStructureNode left,
        JsonStructureNode right,
        List<string?> path,
        List<StructureDifference>? differences,
        Rules rules)
    {
        rules.Budget?.Spend();
        if (left.Children.Count != 0 && rules.Cache.TryGetIdentical(left, right, rules.CompareValues, out var size))
        {
            return new Outcome(size);
        }

        if (TypeOf(left) != TypeOf(right))
        {
            return Difference(differences, rules, StructureMismatchType.TypeMismatch, path, left, right);
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                return Outcome.Node + CompareProperties(left, right, path, differences, rules);

            case JsonValueKind.Array:
                return Outcome.Node + CompareElements(left, right, path, differences, rules);

            default:
                // The cache keeps folded texts for the candidates of a group, which are compared once per candidate;
                // a reported pair is compared once and would only fill it.
                if (rules.CompareValues && !ValueMatcher.MatchValues(left, right, rules.Values, rules.Budget, differences is null ? rules.Cache : null, rules.FuzzyThreshold))
                {
                    // Two strings that fold to equal under Normalized (case, outer white space or Unicode form
                    // only), but the configured rules did not already accept them, report a lighter
                    // ValueCaseMismatch instead of a full ValueMismatch. The pair stays unmatched either way.
                    var mismatchType = ValueMatcher.MatchNormalized(left, right, rules.Cache)
                        ? StructureMismatchType.ValueCaseMismatch
                        : StructureMismatchType.ValueMismatch;
                    return Difference(differences, rules, mismatchType, path, left, right);
                }
                return Outcome.Node;
        }
    }

    // True and False are one Boolean type; every other kind is a type of its own.
    internal static JsonValueKind TypeOf(JsonStructureNode node) =>
        node.ValueKind == JsonValueKind.False ? JsonValueKind.True : node.ValueKind;

    // Properties are paired by name in any order, under the name rule, and so are the properties sharing a name:
    // see PairSameName. An unpaired subtree is one difference at its top. A left and a right orphan whose names
    // fold to one key under Normalized, with neither side holding the other's exact name, pair as one lighter
    // PropertyNameCaseMismatch instead of a full MissingProperty and a full ExtraProperty.
    private static Outcome CompareProperties(
        JsonStructureNode left,
        JsonStructureNode right,
        List<string?> path,
        List<StructureDifference>? differences,
        Rules rules)
    {
        var leftNames = rules.Cache.Names(left);
        var rightNames = rules.Cache.Names(right);

        // The right partner of each left property, with the outcome a group assignment already found.
        var partners = new (int Right, Outcome? Speculated)[left.Children.Count];
        for (var index = 0; index < partners.Length; index++) partners[index] = (-1, null);
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

        var pairedRight = new bool[right.Children.Count];
        foreach (var partner in partners)
        {
            if (partner.Right >= 0) pairedRight[partner.Right] = true;
        }

        // Orphans on each side (no exact-name partner and no exact-name counterpart at all on the other side)
        // that fold to the same key under Normalized are paired by that key. Weighted scoring first selects
        // which surplus occurrences to leave out; equally good selections and the pairs follow document order.
        var caseMatchRight = new int[left.Children.Count];
        for (var index = 0; index < caseMatchRight.Length; index++) caseMatchRight[index] = -1;
        var rightOrphansByKey = new Dictionary<string, List<int>>(NameComparer(JJPropertyNameMatchingMode.Normalized));
        for (var index = 0; index < right.Children.Count; index++)
        {
            var property = right.Children[index];
            if (pairedRight[index] || leftNames.Contains(property.PropertyName!)) continue;
            var key = NameKey(property.PropertyName!, JJPropertyNameMatchingMode.Normalized);
            if (!rightOrphansByKey.TryGetValue(key, out var indices))
            {
                indices = new List<int>();
                rightOrphansByKey[key] = indices;
            }
            indices.Add(index);
        }
        var leftOrphansByKey = new Dictionary<string, List<int>>(NameComparer(JJPropertyNameMatchingMode.Normalized));
        for (var index = 0; index < left.Children.Count; index++)
        {
            var property = left.Children[index];
            if (partners[index].Right >= 0 || rightNames.Contains(property.PropertyName!)) continue;
            var key = NameKey(property.PropertyName!, JJPropertyNameMatchingMode.Normalized);
            if (!rightOrphansByKey.ContainsKey(key)) continue;
            if (!leftOrphansByKey.TryGetValue(key, out var indices))
            {
                indices = new List<int>();
                leftOrphansByKey[key] = indices;
            }
            indices.Add(index);
        }
        foreach (var group in leftOrphansByKey)
        {
            var rightIndices = rightOrphansByKey[group.Key];
            var count = Math.Min(group.Value.Count, rightIndices.Count);
            var selectedLeft = SelectOrphans(group.Value, left, count, rules.Scoring?.MissingWeight ?? 0);
            var selectedRight = SelectOrphans(rightIndices, right, count, rules.Scoring?.AddedWeight ?? 0);
            for (var index = 0; index < count; index++) caseMatchRight[selectedLeft[index]] = selectedRight[index];
        }

        List<int> SelectOrphans(List<int> indices, JsonStructureNode parent, int count, int surplusWeight)
        {
            if (indices.Count == count || rules.Scoring is not { } scoring
                || scoring.PropertyNameCaseMismatchWeight == surplusWeight) return indices;
            // Every case-name pair contributes the sum of its two subtree sizes, with no comparison below it.
            // Selection is therefore independent on each side: replace the surplus cost by the case-name cost.
            // Stable sorting retains the earliest occurrences at equal cost; report the chosen pairs in order.
            return indices.OrderBy(index => (long)(scoring.PropertyNameCaseMismatchWeight - surplusWeight)
                    * rules.Cache.Size(parent.Children[index]))
                .Take(count).OrderBy(index => index).ToList();
        }

        var outcome = default(Outcome);
        for (var index = 0; index < left.Children.Count; index++)
        {
            var property = left.Children[index];
            path.Add(property.PropertyName);
            var partner = partners[index];
            if (partner.Right < 0)
            {
                if (caseMatchRight[index] >= 0)
                {
                    pairedRight[caseMatchRight[index]] = true;
                    outcome += Difference(differences, rules, StructureMismatchType.PropertyNameCaseMismatch, path, property, right.Children[caseMatchRight[index]]);
                }
                else
                {
                    var mismatchType = rightNames.Contains(property.PropertyName!)
                        ? StructureMismatchType.DuplicatePropertyCountMismatch
                        : StructureMismatchType.MissingProperty;
                    outcome += Difference(differences, rules, mismatchType, path, property, null);
                }
            }
            else
            {
                outcome += differences is null && partner.Speculated is { } speculated
                    ? speculated
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
                outcome += Difference(differences, rules, mismatchType, path, null, property);
                path.RemoveAt(path.Count - 1);
            }
        }

        return outcome;
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
        (int Right, Outcome? Speculated)[] partners,
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
                partners[leftIndices[leftIndex]] = (rightIndices[assigned[leftIndex].Right], assigned[leftIndex].Outcome);
            }
        }
    }

    // Elements are paired as a group, wherever they stand in their arrays: see Assign. Strict pairs every element,
    // and any two may be paired. StructureOnly first reduces each side to its distinct element shapes and pairs
    // only those CanPair allows; a pair of different shapes always yields at least one difference. An element or
    // shape without a partner is reported whole.
    private static Outcome CompareElements(
        JsonStructureNode left,
        JsonStructureNode right,
        List<string?> path,
        List<StructureDifference>? differences,
        Rules rules)
    {
        path.Add(null);
        var shapesOnly = rules.Arrays == JJArrayMatchingMode.StructureOnly;
        // Shape equivalence ignores all values below this array, so collapsing repeated shapes stays well-defined.
        if (shapesOnly && rules.CompareValues)
            rules = new Rules(rules.Arrays, rules.Values, false, rules.Budget, rules.Cache, rules.FuzzyThreshold, rules.Scoring);
        var leftShapes = shapesOnly ? DistinctShapes(left, rules.Cache) : left.Children.ToList();
        var rightShapes = shapesOnly ? DistinctShapes(right, rules.Cache) : right.Children.ToList();

        var outcome = default(Outcome);
        // One element on each side is paired when it may be, without identities or a solver.
        if (leftShapes.Count == 1 && rightShapes.Count == 1 && (!shapesOnly || CanPair(leftShapes[0], rightShapes[0], rules)))
        {
            rules.Budget?.Assignment(1, 1);
            outcome = Compare(leftShapes[0], rightShapes[0], path, differences, rules);
            path.RemoveAt(path.Count - 1);
            return outcome;
        }

        var assigned = Assign(leftShapes, rightShapes, !shapesOnly, path, rules);
        var pairedRight = new HashSet<int>();
        for (var leftIndex = 0; leftIndex < leftShapes.Count; leftIndex++)
        {
            if (assigned[leftIndex].Right >= 0)
            {
                pairedRight.Add(assigned[leftIndex].Right);
                outcome += differences is null
                    ? assigned[leftIndex].Outcome
                    : Compare(leftShapes[leftIndex], rightShapes[assigned[leftIndex].Right], path, differences, rules);
            }
            else
            {
                outcome += Difference(differences, rules, StructureMismatchType.MissingArrayElementShape, path, leftShapes[leftIndex], null);
            }
        }

        for (var rightIndex = 0; rightIndex < rightShapes.Count; rightIndex++)
        {
            if (!pairedRight.Contains(rightIndex))
            {
                outcome += Difference(differences, rules, StructureMismatchType.ExtraArrayElementShape, path, null, rightShapes[rightIndex]);
            }
        }

        path.RemoveAt(path.Count - 1);
        return outcome;
    }

    // Pairs the occurrences of a group one to one and returns for each left one the index of its right partner, or
    // -1, with the outcome of the pair. Among same-named properties every pair may be made; among array element
    // shapes only those CanPair allows. The pairs are chosen to match the most nodes over the whole group, which is
    // the same as leaving the fewest nodes unmatched: every pair that may be made is counted once and the group is
    // assigned as a whole, whatever its size, so the count is the maximum any pairing could reach. Only outcomes are
    // computed here; a caller that reports differences compares the pairs taken once more, which costs one
    // comparison per pair instead of one per candidate.
    private static (int Right, Outcome Outcome)[] Assign(
        List<JsonStructureNode> left,
        List<JsonStructureNode> right,
        bool pairUnrelated,
        List<string?> path,
        Rules rules)
    {
        var assigned = new (int Right, Outcome Outcome)[left.Count];
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

        var outcomes = new Outcome[left.Count, right.Count];
        var matchedCounts = new int[left.Count, right.Count];
        var sameShape = new bool[left.Count, right.Count];
        // The tie levels of SolveAssignment, in their order; the weighted level exists under Weighted scoring only.
        var levels = new long[,]?[] { new long[left.Count, right.Count], new long[left.Count, right.Count], new long[left.Count, right.Count], rules.Scoring is null ? null : new long[left.Count, right.Count] };
        for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            for (var rightIndex = 0; rightIndex < right.Count; rightIndex++)
            {
                if (!pairUnrelated && !CanPair(left[leftIndex], right[rightIndex], rules))
                {
                    matchedCounts[leftIndex, rightIndex] = -1;
                    continue;
                }
                var outcome = outcomes[leftIndex, rightIndex] = Compare(left[leftIndex], right[rightIndex], path, null, rules);
                matchedCounts[leftIndex, rightIndex] = outcome.Matched;
                // A pair has no differences exactly when every node of both subtrees is matched.
                sameShape[leftIndex, rightIndex] = outcome.Matched == leftSizes[leftIndex] && outcome.Matched == rightSizes[rightIndex];
                levels[0]![leftIndex, rightIndex] = outcome.Differences;
                levels[1]![leftIndex, rightIndex] = outcome.TypeMismatches;
                levels[2]![leftIndex, rightIndex] = -outcome.CaseOnly;
                if (levels[3] is { } weighted)
                {
                    // Start from the constant cost of leaving every subtree unpaired. A pair replaces
                    // its Missing and Added costs with its own differences. Including that saving makes
                    // this tie level agree with the final score even when group sizes differ.
                    weighted[leftIndex, rightIndex] = outcome.Weighted
                        - (long)rules.Scoring!.MissingWeight * leftSizes[leftIndex]
                        - (long)rules.Scoring.AddedWeight * rightSizes[rightIndex];
                }
            }
        }
        var partners = SolveAssignment(matchedCounts, sameShape, levels, rules.Budget);
        for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            if (partners[leftIndex] >= 0)
            {
                assigned[leftIndex] = (partners[leftIndex], outcomes[leftIndex, partners[leftIndex]]);
            }
        }
        return assigned;
    }

    // The assignment that matches the most nodes over the whole group; among those, the one pairing the most
    // occurrences of the same shape, which have no differences; among those, the one making the most pairs; among
    // those, the one reporting the fewest differences, then the fewest type mismatches, then the most case-only
    // ones, then, under Weighted scoring, the lightest weighted mass. Among equally good assignments, the first left
    // occurrence gets the earliest right partner that any of them gives it, then the second, and so on; having a
    // partner comes before having none.
    // The solver is checked against an exhaustive search through this entry, which reads the tie rule from each
    // pair's differences; a difference it does not know is neither a type mismatch nor case-only.
    private static int[] BestAssignment(int[,] matchedCounts, List<StructureDifference>?[,] pairDifferences)
    {
        var rows = matchedCounts.GetLength(0);
        var columns = matchedCounts.GetLength(1);
        var sameShape = new bool[rows, columns];
        var levels = new long[,]?[] { new long[rows, columns], new long[rows, columns], new long[rows, columns], null };
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var differences = pairDifferences[row, column];
                sameShape[row, column] = differences is { Count: 0 };
                if (differences is null) continue;
                var outcome = default(Outcome);
                foreach (var difference in differences) outcome += Outcome.Of(difference?.MismatchType ?? StructureMismatchType.ValueMismatch, 0);
                levels[0]![row, column] = outcome.Differences;
                levels[1]![row, column] = outcome.TypeMismatches;
                levels[2]![row, column] = -outcome.CaseOnly;
            }
        }
        return SolveAssignment(matchedCounts, sameShape, levels, null);
    }

    // The table is padded to a square with rows and columns that stand for "no partner", and solved with the
    // Hungarian algorithm, in O(n^3), once per level of the tie rule: first the nodes matched, the same-shape pairs
    // and the pairs, encoded in one cost; then each further level given, a cost per pair to be minimized. The
    // potentials of a solve tell which cells can be part of a best assignment at its level (the tight ones), so
    // each later level is solved over those cells alone, every cell outside them costing more than any assignment
    // inside, and keeps every earlier level at its best. A level whose cost is the same in every pair still open
    // cannot change the choice and is skipped, so a group without differences pays for one solve. The positional
    // rule is then applied by rearranging within the cells left, without solving again: O(n^4) at worst.
    private static int[] SolveAssignment(int[,] matchedCounts, bool[,] sameShape, long[,]?[] levels, ComparisonBudget? budget)
    {
        var leftCount = matchedCounts.GetLength(0);
        var rightCount = matchedCounts.GetLength(1);
        var size = Math.Max(leftCount, rightCount);
        bool IsPair(int row, int column) => row < leftCount && column < rightCount && matchedCounts[row, column] >= 0;

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
        var (tight, rowOfColumn) = Solve(cost, null);

        foreach (var level in levels)
        {
            if (level is null) continue;
            // The number of pairs is settled by now, so a level with one cost for every open pair cannot vary.
            long? common = null;
            var varies = false;
            long span = 0;
            for (var row = 0; row < leftCount; row++)
            {
                for (var column = 0; column < rightCount; column++)
                {
                    if (!tight[row, column] || !IsPair(row, column)) continue;
                    span = Math.Max(span, Math.Abs(level[row, column]));
                    if (common is null) common = level[row, column];
                    else if (common != level[row, column]) varies = true;
                }
            }
            if (!varies) continue;

            // Any assignment within the open cells costs between -span and span per cell, so one forbidden cell
            // outweighs every such assignment.
            var forbidden = (2 * span + 1) * (size + 1);
            var next = new long[size, size];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                {
                    next[row, column] = !tight[row, column] ? forbidden : IsPair(row, column) ? level[row, column] : 0;
                }
            }
            (tight, rowOfColumn) = Solve(next, tight);
        }

        // Runs the Hungarian algorithm over the cost table and returns the cells that can be part of a best
        // assignment, within the cells allowed, and the best assignment it found as the row of each column.
        (bool[,] Tight, int[] RowOfColumn) Solve(long[,] table, bool[,]? within)
        {
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

                        var reduced = table[currentRow - 1, other - 1] - rowPotential[currentRow] - columnPotential[other];
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
            var tightCells = new bool[size, size];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                {
                    tightCells[row, column] = (within is null || within[row, column])
                        && table[row, column] - rowPotential[row + 1] - columnPotential[column + 1] == 0;
                }
            }
            var assignment = new int[size];
            for (var column = 0; column < size; column++) assignment[column] = rowOfColumn[column + 1] - 1;
            return (tightCells, assignment);
        }

        var columnOf = new int[size];
        var rowOf = new int[size];
        for (var column = 0; column < size; column++)
        {
            rowOf[column] = rowOfColumn[column];
            columnOf[rowOf[column]] = column;
        }

        // Columns settled for an earlier row.
        var settled = new bool[size];

        // Gives the row another tight column that is not settled, moving later rows out of the way when needed.
        bool TryPlace(int row, bool[] seen)
        {
            budget?.Spend(size);
            for (var column = 0; column < size; column++)
            {
                if (settled[column] || seen[column] || !tight[row, column])
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
                if (!settled[column] && IsPair(row, column) && tight[row, column] && TryTake(row, column))
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
