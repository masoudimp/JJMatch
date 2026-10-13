using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace JJMatch;

/// <summary>Compares the primitive values of two <see cref="JsonStructureNode"/>s under a <see cref="JJValueMatchingMode"/>.</summary>
internal static class ValueMatcher
{
    /// <summary>
    /// Returns whether the values of <paramref name="left"/> and <paramref name="right"/> are equal under <paramref name="mode"/>.
    /// Identical values are always equal; each flag of the mode is one further rule, and the values are equal when
    /// any rule that is set holds, so <see cref="JJValueMatchingMode.None"/> accepts identical values only. Neither node is changed. A cache keeps the
    /// normalized and folded texts of a node across the many pairs of a group. The fuzzy threshold is the least
    /// similarity <see cref="JJValueMatchingMode.Fuzzy"/> accepts, from 0 to 1; the parameter overrides every
    /// other source of it, the public API passes <see cref="JJMatchSettings.FuzzyThreshold"/>, and omitting it
    /// gives <see cref="DefaultFuzzyThreshold"/>.
    /// </summary>
    internal static bool MatchValues(JsonStructureNode left, JsonStructureNode right, JJValueMatchingMode mode,
        ComparisonBudget? budget = null, ComparisonCache? cache = null, double fuzzyThreshold = DefaultFuzzyThreshold)
    {
        budget?.Spend(1L + (left.Value?.Length ?? 0) + (right.Value?.Length ?? 0));
        // Each further mode adds one clause here, so that a pair matches when any enabled rule accepts it.
        return MatchExact(left, right)
            || ((mode & JJValueMatchingMode.Normalized) != 0 && MatchNormalized(left, right, cache))
            || ((mode & JJValueMatchingMode.Fuzzy) != 0 && MatchFuzzy(left, right, fuzzyThreshold, budget, cache))
            || ((mode & JJValueMatchingMode.Numeric) != 0 && MatchNumeric(left, right, budget));
    }

    // Two primitives of one JSON kind whose values are identical: strings by their decoded text and numbers by their
    // raw text, both ordinal and case-sensitive, so nothing is trimmed, folded or converted; true, false and null
    // each equal only themselves. A string without a Value, an object, an array and an undefined element never
    // match under this rule, so an undecodable string is not taken for null.
    private static bool MatchExact(JsonStructureNode left, JsonStructureNode right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }
        switch (left.ValueKind)
        {
            case JsonValueKind.String:
            case JsonValueKind.Number:
                return left.Value is not null && right.Value is not null && string.Equals(left.Value, right.Value, StringComparison.Ordinal);
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return true;
            default:
                return false;
        }
    }

    // Two strings whose normalized texts are equal ignoring case, by the invariant simple case mapping of
    // OrdinalIgnoreCase, so the current culture plays no part. Any other kind, and a string without a Value, never
    // matches under this rule. Also used, regardless of the configured value mode, to recognize a failed value
    // comparison as a lighter ValueCaseMismatch.
    internal static bool MatchNormalized(JsonStructureNode left, JsonStructureNode right, ComparisonCache? cache) =>
        left.ValueKind == JsonValueKind.String
        && right.ValueKind == JsonValueKind.String
        && left.Value is not null
        && right.Value is not null
        && string.Equals(Normalized(left, cache), Normalized(right, cache), StringComparison.OrdinalIgnoreCase);

    private static string Normalized(JsonStructureNode node, ComparisonCache? cache) =>
        cache is null ? Normalize(node.Value!) : cache.Normalized(node);

    // Two strings whose folded texts (normalized as Normalized does, then upper-cased) are a few edits apart: their
    // similarity, one minus the edits per UTF-16 unit of the longer text, is at least the threshold. At the default
    // 0.8 that is one edit for every five units, so "Jon Smith" equals "John Smith" (one edit in ten units) and
    // "Jon" does not equal "John" (one edit in four). An edit is an inserted, deleted or replaced unit, or two
    // adjacent units swapped. Any other kind, and a string without a Value, never matches under this rule.
    private static bool MatchFuzzy(JsonStructureNode left, JsonStructureNode right, double threshold, ComparisonBudget? budget, ComparisonCache? cache)
    {
        if (left.ValueKind != JsonValueKind.String || right.ValueKind != JsonValueKind.String || left.Value is null || right.Value is null)
        {
            return false;
        }
        var leftText = cache is null ? Fold(left.Value) : cache.Folded(left);
        var rightText = cache is null ? Fold(right.Value) : cache.Folded(right);
        var allowed = AllowedEdits(Math.Max(leftText.Length, rightText.Length), threshold);

        // The lengths alone can rule the pair out: every unit of difference in length costs one edit. Equal folded
        // texts are zero edits apart, so no table is needed for them either.
        if (Math.Abs(leftText.Length - rightText.Length) > allowed) return false;
        if (string.Equals(leftText, rightText, StringComparison.Ordinal)) return true;
        budget?.Spend((long)Math.Max(leftText.Length, rightText.Length) * (2 * allowed + 1));
        return Distance(leftText, rightText, allowed, budget, cache) <= allowed;
    }

    /// <summary>The least similarity <see cref="JJValueMatchingMode.Fuzzy"/> accepts unless told otherwise: one edit for every five units of the longer text.</summary>
    internal const double DefaultFuzzyThreshold = 0.8;

    // The most edits a text of the given length tolerates: the units it may lose before fewer than threshold × length
    // remain. The units that must remain are rounded up, so that a threshold written in decimal (0.8, 0.75) keeps
    // its exact boundary: 0.8 × 5 is 4 units kept and one edit allowed, although 1 − 0.8 is below 0.2 in binary.
    private static int AllowedEdits(int length, double threshold) =>
        length - (int)Math.Ceiling(threshold * length);

    // The text Fuzzy compares: Normalize, then invariant upper case. Casing can map supplementary code points;
    // distance is measured in UTF-16 units of the resulting text, independently of the current culture.
    private static string Fold(string value) => Normalize(value).ToUpperInvariant();

    // The optimal string alignment distance between two texts: the fewest insertions, deletions, substitutions and
    // transpositions of adjacent units that turn one into the other, where no substring is edited twice. Three
    // rows of the table are kept, the one being filled and the two before it, so the memory is linear in the
    // shorter text. The distance is only needed up to a bound, so only the band of cells within the bound of the
    // diagonal is filled: reaching a cell outside it takes more insertions or deletions than the bound, so such a
    // cell is above it, and the cells just outside the band are set above it before they are read. Once every cell
    // of a row is above the bound the result is too, because no cell is ever smaller than the smallest of the row
    // before, so the search stops and returns a value above the bound.
    private static int Distance(string left, string right, int bound, ComparisonBudget? budget, ComparisonCache? cache)
    {
        if (left.Length < right.Length)
        {
            (left, right) = (right, left);
        }
        var above = bound + 1;
        var rows = cache is null ? new int[3][] : cache.Rows(right.Length + 1);
        var previousPrevious = rows[0] ??= new int[right.Length + 1];
        var previous = rows[1] ??= new int[right.Length + 1];
        var current = rows[2] ??= new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = Math.Min(column, above);
        }

        for (var row = 1; row <= left.Length; row++)
        {
            budget?.Spend(0); // Work was reserved up front; cancellation is still observed during the table.
            var first = Math.Max(1, row - bound);
            var last = Math.Min(right.Length, row + bound);
            current[first - 1] = first == 1 ? Math.Min(row, above) : above;
            if (last < right.Length) current[last + 1] = above;
            // Column zero counts too: against the empty text the band holds no other cell, and the row is its value.
            var smallest = current[first - 1];
            for (var column = first; column <= last; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(previous[column] + 1, current[column - 1] + 1), previous[column - 1] + cost);
                if (row > 1 && column > 1 && left[row - 1] == right[column - 2] && left[row - 2] == right[column - 1])
                {
                    value = Math.Min(value, previousPrevious[column - 2] + 1);
                }
                current[column] = value;
                smallest = Math.Min(smallest, value);
            }
            if (smallest > bound)
            {
                return above;
            }
            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }
        return previous[right.Length];
    }

    // Two numbers that denote one quantity, whatever their spelling: 123 and 123.0, 1e2 and 100, -0 and 0. Each raw
    // text is reduced to its sign, significant digits and power of ten, which are equal exactly when the quantities
    // are, so no floating-point or decimal arithmetic takes part and no digit or exponent is too large. Any other
    // kind never matches under this rule.
    private static bool MatchNumeric(JsonStructureNode left, JsonStructureNode right, ComparisonBudget? budget)
    {
        if (left.ValueKind != JsonValueKind.Number || right.ValueKind != JsonValueKind.Number || left.Value is null || right.Value is null)
        {
            return false;
        }
        // Conservative allowance for arbitrary-precision decimal conversion; not a wall-clock estimate.
        budget?.Spend((long)left.Value.Length * left.Value.Length + (long)right.Value.Length * right.Value.Length);
        var (leftNegative, leftDigits, leftExponent) = Quantity(left.Value);
        var (rightNegative, rightDigits, rightExponent) = Quantity(right.Value);
        return leftNegative == rightNegative
            && string.Equals(leftDigits, rightDigits, StringComparison.Ordinal)
            && leftExponent == rightExponent;
    }

    // The quantity a JSON number denotes, as Digits × 10^Exponent with the sign apart: Digits are the significant
    // digits, with no leading or trailing zero, so that one quantity has one form. Zero has no digits, no sign and
    // exponent zero, so -0, 0.0 and 0e5 are one. The text is a valid JSON number, already checked by the parser:
    // an optional minus, an integer part, an optional fraction and an optional exponent. Only the exponent is
    // parsed, as a BigInteger, so large exponents remain exact within available resources. Mantissa processing is
    // linear; exponent conversion has the runtime's BigInteger parsing cost. Nothing is raised to a power.
    private static (bool Negative, string Digits, BigInteger Exponent) Quantity(string raw)
    {
        var start = raw[0] == '-' ? 1 : 0;
        var exponentAt = raw.IndexOfAny(ExponentMarks);
        var mantissaEnd = exponentAt < 0 ? raw.Length : exponentAt;
        var pointAt = raw.IndexOf('.', start, mantissaEnd - start);
        var fractionLength = pointAt < 0 ? 0 : mantissaEnd - pointAt - 1;
        var digits = pointAt < 0
            ? raw.Substring(start, mantissaEnd - start)
            : raw.Substring(start, pointAt - start) + raw.Substring(pointAt + 1, fractionLength);

        var first = 0;
        while (first < digits.Length && digits[first] == '0')
        {
            first++;
        }
        if (first == digits.Length)
        {
            return (false, string.Empty, BigInteger.Zero);
        }
        var last = digits.Length - 1;
        while (digits[last] == '0')
        {
            last--;
        }

        // Every digit after the point lowers the power by one, and every trailing zero dropped raises it by one.
        var exponent = exponentAt < 0
            ? BigInteger.Zero
            : BigInteger.Parse(raw.Substring(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        exponent += digits.Length - 1 - last - fractionLength;
        return (start == 1, digits.Substring(first, last - first + 1), exponent);
    }

    private static readonly char[] ExponentMarks = { 'e', 'E' };

    /// <summary>
    /// The normalized form of a string value: leading and trailing white space removed, then Unicode NFC. Case is
    /// kept, so normalized texts are compared with <see cref="StringComparison.OrdinalIgnoreCase"/>. A string holding a
    /// lone surrogate, or the noncharacter U+FFFE or U+FFFF, is rejected by <see cref="string.Normalize(NormalizationForm)"/>,
    /// so such a string is only trimmed. The input is never changed,
    /// and it is returned as is when nothing needs to change.
    /// </summary>
    internal static string Normalize(string value)
    {
        var trimmed = value.Trim();
        return IsWellFormed(trimmed) ? trimmed.Normalize(NormalizationForm.FormC) : trimmed;
    }

    // Every surrogate is half of a pair and neither U+FFFE nor U+FFFF occurs: exactly the texts string.Normalize
    // accepts.
    private static bool IsWellFormed(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(text[index]) || text[index] >= '\uFFFE')
            {
                return false;
            }
        }
        return true;
    }
}
