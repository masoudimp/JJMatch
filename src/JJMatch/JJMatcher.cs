using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace JJMatch;

/// <summary>Compares JSON documents using explicit structural and primitive value rules.</summary>
public static class JJMatcher
{
    /// <summary>Compares two JSON documents and returns their similarity and differences.</summary>
    /// <param name="json1">The left JSON document. Any valid JSON root is accepted.</param>
    /// <param name="json2">The right JSON document.</param>
    /// <param name="options">Use StructuralMatching to ignore primitive values; null or All compares structure and values.</param>
    /// <param name="settings">Optional array, value and property name rules, scoring configuration and resource limits. Defaults to Strict arrays, identical values, identical names and Standard scoring.</param>
    /// <param name="cancellationToken">Cancels comparison cooperatively.</param>
    /// <exception cref="ArgumentNullException">An input is null.</exception>
    /// <exception cref="ArgumentException">Options or settings are invalid.</exception>
    /// <exception cref="JsonException">An input is malformed or exceeds the configured parsing depth.</exception>
    /// <exception cref="InvalidOperationException">A configured resource limit is exceeded.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static JJMatchResult Match(string json1, string json2, JJMatchOptions? options = null,
        JJMatchSettings? settings = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (json1 is null) throw new ArgumentNullException(nameof(json1));
        if (json2 is null) throw new ArgumentNullException(nameof(json2));
        var flags = options ?? JJMatchOptions.All;
        const JJMatchOptions known = JJMatchOptions.StructuralMatching | JJMatchOptions.ValueMatching;
        if ((flags & ~known) != 0) throw new ArgumentOutOfRangeException(nameof(options));
        var config = (settings ?? new JJMatchSettings()).Snapshot();
        var features = flags & (JJMatchOptions.StructuralMatching | JJMatchOptions.ValueMatching);
        var compareValues = features != JJMatchOptions.StructuralMatching;
        var structuralDiagnostics = features != JJMatchOptions.ValueMatching;

        var budget = new ComparisonBudget(config, cancellationToken);
        using var leftDocument = Parse(json1, config, budget);
        using var rightDocument = Parse(json2, config, budget);
        var left = JsonStructureTraverser.Traverse(leftDocument.RootElement, budget: budget);
        var right = JsonStructureTraverser.Traverse(rightDocument.RootElement, budget: budget);
        var cache = new ComparisonCache(config.ArrayMode, budget, config.PropertyNameMode);
        var total = StructureMatcher.CountNodes(left, config.ArrayMode, budget, cache)
            + StructureMatcher.CountNodes(right, config.ArrayMode, budget, cache);
        var match = StructureMatcher.Match(left, right, config.ArrayMode, config.ValueMode, compareValues, budget, cache, config.PropertyNameMode, config.FuzzyThreshold);
        // A value-aware full match is a structural full match too, and a structural count cannot exceed the total.
        var structural = structuralDiagnostics && compareValues && match.Differences.Count != 0
            ? StructureMatcher.Match(left, right, config.ArrayMode, config.ValueMode, false, budget, cache, config.PropertyNameMode)
            : null;
        var score = Score(match, total, config, budget, cache);
        double? structuralScore = !structuralDiagnostics ? null : structural is null ? score : Score(structural, total, config, budget, cache);

        var differences = new List<JJDifference>(match.Differences.Count);
        if (match.Differences.Count != 0)
        {
            var leftLocations = Locations(left, leftDocument.RootElement, match.Differences.Select(d => d.Left), budget);
            var rightLocations = Locations(right, rightDocument.RootElement, match.Differences.Select(d => d.Right), budget);
            foreach (var difference in match.Differences)
            {
                budget.Spend();
                var l = difference.Left is null ? default : leftLocations[difference.Left];
                var r = difference.Right is null ? default : rightLocations[difference.Right];
                var kind = Kind(difference);
                differences.Add(new JJDifference(kind, config.Scoring.AppliedWeight(kind), l.Path, r.Path,
                    difference.Left is null ? null : l.Element, difference.Right is null ? null : r.Element));
            }
        }
        return new JJMatchResult(score, differences.AsReadOnly(), structuralScore, compareValues ? score : null,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    // Matched nodes are a one-to-one correspondence, so 2M <= total. Bounded integer node counts keep both
    // endpoints exact in double arithmetic: no rounding/clamping is used to hide a mismatch.
    private static double Dice(int matched, int total) => 200d * matched / total;

    // The public kind of an internal difference: a name-case pair decides first, then the side that is absent, then the category.
    private static JJDifferenceKind Kind(StructureDifference difference) =>
        difference.MismatchType == StructureMismatchType.PropertyNameCaseMismatch ? JJDifferenceKind.PropertyNameCaseMismatch
        : difference.Left is null ? JJDifferenceKind.Added
        : difference.Right is null ? JJDifferenceKind.Missing
        : difference.MismatchType == StructureMismatchType.ValueMismatch ? JJDifferenceKind.ValueMismatch
        : difference.MismatchType == StructureMismatchType.ValueCaseMismatch ? JJDifferenceKind.ValueCaseMismatch
        : JJDifferenceKind.TypeMismatch;

    // Standard scoring is Dice over the node counts. Weighted scoring is Dice over node weights: a matched node weighs
    // MaxWeight on each side, and an unmatched node the weight of the kind of the difference reported above it. Every
    // unmatched node lies under exactly one difference (nothing below a difference is compared or reported again), so
    // the weighted unmatched mass is a sum over the differences of their subtree sizes, counted as the denominator
    // counts them, and nothing is counted twice. With every weight at MaxWeight the quotient is 200M / (L + R) again,
    // computed as one correctly rounded division of the same rational number, so the two modes then agree exactly.
    private static double Score(StructureMatchResult match, int total, JJMatchSettings config, ComparisonBudget budget, ComparisonCache cache)
    {
        if (config.Scoring.Mode == JJScoringMode.Standard) return Dice(match.MatchedCount, total);
        long unmatched = 0;
        foreach (var difference in match.Differences)
        {
            budget.Spend();
            long weight = config.Scoring.Weight(Kind(difference));
            if (difference.Left is not null) unmatched += weight * StructureMatcher.CountNodes(difference.Left, config.ArrayMode, budget, cache);
            if (difference.Right is not null) unmatched += weight * StructureMatcher.CountNodes(difference.Right, config.ArrayMode, budget, cache);
        }
        // Roots exist on both sides, so the denominator is positive: either a node matched or the roots differ.
        var matched = 2L * JJScoringSettings.MaxWeight * match.MatchedCount;
        return 100d * matched / (matched + unmatched);
    }

    private static JsonDocument Parse(string text, JJMatchSettings settings, ComparisonBudget budget)
    {
        budget.Spend();
        if (text.Length > settings.MaxInputLength)
            throw new InvalidOperationException("Input exceeded JJMatchSettings.MaxInputLength.");
        var options = new JsonDocumentOptions { MaxDepth = settings.MaxDepth };
        var document = JsonDocument.Parse(text, options);
        if (document.RootElement.ValueKind != JsonValueKind.String) return document;
        // A string root holding a serialized object or array is decoded, one layer only. Any other string root,
        // undecodable or unparsable ones included, stays a literal string, so every JSON root is still accepted.
        string decoded;
        try
        {
            decoded = document.RootElement.GetString()!;
        }
        catch (InvalidOperationException)
        {
            return document;
        }
        var start = decoded.AsSpan().TrimStart(" \t\r\n".AsSpan());
        if (start.Length == 0 || (start[0] != '{' && start[0] != '[')) return document;
        budget.Spend();
        try
        {
            var inner = JsonDocument.Parse(decoded, options);
            document.Dispose();
            return inner;
        }
        catch (JsonException)
        {
            return document;
        }
    }

    // Revisit only the original positions, not comparison semantics. Snapshot just the selected differences,
    // while their source documents are alive. Duplicate occurrence suffixes include every duplicated ancestor.
    // Only the containers above a wanted node are entered; everything else is skipped without a path.
    private static Dictionary<JsonStructureNode, (string Path, JsonElement Element)> Locations(
        JsonStructureNode root, JsonElement element, IEnumerable<JsonStructureNode?> wanted, ComparisonBudget budget)
    {
        var targets = new HashSet<JsonStructureNode>();
        foreach (var node in wanted)
        {
            if (node is not null) targets.Add(node);
        }
        var ancestors = new HashSet<JsonStructureNode>();
        Mark(root);
        var locations = new Dictionary<JsonStructureNode, (string Path, JsonElement Element)>(targets.Count);
        if (ancestors.Contains(root)) Add(root, element, "$");
        return locations;

        // Whether the subtree holds a wanted node, remembering every node that does.
        bool Mark(JsonStructureNode node)
        {
            budget.Spend();
            var holds = targets.Contains(node);
            foreach (var child in node.Children)
            {
                holds |= Mark(child);
            }
            if (holds) ancestors.Add(node);
            return holds;
        }

        void Add(JsonStructureNode node, JsonElement value, string path)
        {
            budget.Spend(1L + path.Length);
            if (targets.Contains(node)) locations.Add(node, (path, value));
            if (node.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    var child = node.Children[index];
                    if (ancestors.Contains(child))
                        Add(child, item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
                    index++;
                }
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var child in node.Children)
                {
                    counts.TryGetValue(child.PropertyName!, out var count);
                    counts[child.PropertyName!] = count + 1;
                }
                var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                var index = 0;
                foreach (var property in value.EnumerateObject())
                {
                    var child = node.Children[index++];
                    var name = child.PropertyName!;
                    var duplicated = counts[name] > 1;
                    var occurrence = 0;
                    if (duplicated)
                    {
                        occurrences.TryGetValue(name, out occurrence);
                        occurrences[name] = ++occurrence;
                    }
                    if (!ancestors.Contains(child)) continue;
                    budget.Spend(name.Length);
                    var childPath = path + StructureMatcher.PropertyPath(name);
                    if (duplicated) childPath += "#" + occurrence.ToString(CultureInfo.InvariantCulture);
                    Add(child, property.Value, childPath);
                }
            }
        }
    }
}
