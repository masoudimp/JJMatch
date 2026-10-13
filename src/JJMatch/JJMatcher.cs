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
    /// <param name="settings">Optional array/value rules and resource limits. Defaults to Strict arrays and Exact values.</param>
    /// <param name="cancellationToken">Cancels comparison cooperatively.</param>
    /// <exception cref="ArgumentNullException">An input is null.</exception>
    /// <exception cref="ArgumentException">Options or settings are invalid.</exception>
    /// <exception cref="JsonException">An input is malformed or exceeds the configured parsing depth.</exception>
    /// <exception cref="InvalidOperationException">A configured resource limit is exceeded.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static JJMatchResult Match(string json1, string json2, JJMatchOptions? options = null,
        JJMatchSettings? settings = null, CancellationToken cancellationToken = default)
    {
        if (json1 is null) throw new ArgumentNullException(nameof(json1));
        if (json2 is null) throw new ArgumentNullException(nameof(json2));
        var flags = options ?? JJMatchOptions.All;
        const JJMatchOptions known = JJMatchOptions.StructuralMatching | JJMatchOptions.ValueMatching;
        if ((flags & ~known) != 0) throw new ArgumentOutOfRangeException(nameof(options));
        var config = (settings ?? new JJMatchSettings()).Snapshot();
        var features = flags & (JJMatchOptions.StructuralMatching | JJMatchOptions.ValueMatching);
        var compareValues = features != JJMatchOptions.StructuralMatching;
        var structuralDiagnostics = features != JJMatchOptions.ValueMatching;
        if (compareValues && config.ValueMode == JJValueMatchingMode.None)
            throw new ArgumentException("Select a value rule, or use StructuralMatching to ignore values.", nameof(settings));

        var budget = new ComparisonBudget(config, cancellationToken);
        using var leftDocument = Parse(json1, config, budget);
        using var rightDocument = Parse(json2, config, budget);
        var left = JsonStructureTraverser.Traverse(leftDocument.RootElement, budget: budget);
        var right = JsonStructureTraverser.Traverse(rightDocument.RootElement, budget: budget);
        var cache = new ComparisonCache(config.ArrayMode, config.ValueMode, budget);
        var total = StructureMatcher.CountNodes(left, config.ArrayMode, budget, cache)
            + StructureMatcher.CountNodes(right, config.ArrayMode, budget, cache);
        var match = StructureMatcher.Match(left, right, config.ArrayMode, config.ValueMode, compareValues, budget, cache);
        var score = Dice(match.MatchedCount, total);
        double? structuralScore = null;
        if (structuralDiagnostics)
        {
            // A value-aware full match is a structural full match too, and a structural count cannot exceed the total.
            structuralScore = compareValues && match.Differences.Count != 0
                ? Dice(StructureMatcher.Match(left, right, config.ArrayMode, config.ValueMode, false, budget, cache).MatchedCount, total)
                : score;
        }

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
                var kind = difference.Left is null ? JJDifferenceKind.Added
                    : difference.Right is null ? JJDifferenceKind.Missing
                    : difference.MismatchType == StructureMismatchType.ValueMismatch ? JJDifferenceKind.ValueMismatch
                    : JJDifferenceKind.TypeMismatch;
                differences.Add(new JJDifference(kind, l.Path, r.Path,
                    difference.Left is null ? null : l.Element, difference.Right is null ? null : r.Element));
            }
        }
        return new JJMatchResult(score, differences.AsReadOnly(), structuralScore, compareValues ? score : null);
    }

    // Matched nodes are a one-to-one correspondence, so 2M <= total. Bounded integer node counts keep both
    // endpoints exact in double arithmetic: no rounding/clamping is used to hide a mismatch.
    private static double Dice(int matched, int total) => 200d * matched / total;

    private static JsonDocument Parse(string text, JJMatchSettings settings, ComparisonBudget budget)
    {
        budget.Spend();
        if (text.Length > settings.MaxInputLength)
            throw new InvalidOperationException("Input exceeded JJMatchSettings.MaxInputLength.");
        var options = new JsonDocumentOptions { MaxDepth = settings.MaxDepth };
        var document = JsonDocument.Parse(text, options);
        if (!settings.ParseStringifiedJson || document.RootElement.ValueKind != JsonValueKind.String) return document;
        // One layer only. Explicit opt-in avoids reinterpreting an ordinary JSON string unexpectedly.
        using (document)
        {
            string decoded;
            try
            {
                decoded = document.RootElement.GetString()!;
            }
            catch (InvalidOperationException exception)
            {
                throw new JsonException("The outer JSON string must decode to valid Unicode JSON text.", exception);
            }
            budget.Spend();
            if (decoded.Length > settings.MaxInputLength)
                throw new InvalidOperationException("Decoded input exceeded JJMatchSettings.MaxInputLength.");
            return JsonDocument.Parse(decoded, options);
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
