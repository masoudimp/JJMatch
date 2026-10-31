using System.ComponentModel;
using System.Text.Json;

namespace JJMatch;

/// <summary>Optional comparison rules and resource limits. Configure before calling <see cref="JJMatcher.Match"/>, or load them with <see cref="FromJson"/>.</summary>
public sealed partial class JJMatchSettings
{
    /// <summary>A fresh settings instance with the original defaults on each access.</summary>
    public static JJMatchSettings Default => new JJMatchSettings();

    /// <summary>A fresh instance with normalized strings and numeric quantities; names remain exact and fuzzy is off.</summary>
    public static JJMatchSettings Normalized => new JJMatchSettings
    {
        StringComparison = JJStringComparison.Normalized,
        NumberComparison = JJNumberComparison.Numeric
    };

    /// <summary>A fresh instance comparing structure throughout the document.</summary>
    public static JJMatchSettings StructureOnly => new JJMatchSettings { Mode = JJComparisonMode.StructureOnly };

    /// <summary>Comparison scope. Explicit legacy options passed to Match override this property.</summary>
    public JJComparisonMode Mode { get; set; } = JJComparisonMode.Full;

    /// <summary>Compute a separate structural score. True preserves the original default; false avoids the extra comparison.</summary>
    public bool IncludeStructuralScore { get; set; } = true;

    /// <summary>Decode strings containing JSON objects or arrays, at the root and inside documents. Defaults to true.</summary>
    public bool DecodeStringifiedJson { get; set; } = true;

    /// <summary>String rule. Shares state with ValueMode; assigning it replaces only the string flags.</summary>
    public JJStringComparison StringComparison
    {
        get => (ValueMode & JJValueMatchingMode.Fuzzy) != 0 ? JJStringComparison.Fuzzy
            : (ValueMode & JJValueMatchingMode.Normalized) != 0 ? JJStringComparison.Normalized : JJStringComparison.Exact;
        set
        {
            if (value < JJStringComparison.Exact || value > JJStringComparison.Fuzzy)
                throw new ArgumentOutOfRangeException(nameof(StringComparison));
            ValueMode = (ValueMode & ~(JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy))
                | (value == JJStringComparison.Fuzzy ? JJValueMatchingMode.Fuzzy
                    : value == JJStringComparison.Normalized ? JJValueMatchingMode.Normalized : JJValueMatchingMode.None);
        }
    }

    /// <summary>Number rule. Shares state with ValueMode; assigning it replaces only the numeric flag.</summary>
    public JJNumberComparison NumberComparison
    {
        get => (ValueMode & JJValueMatchingMode.Numeric) != 0 ? JJNumberComparison.Numeric : JJNumberComparison.Exact;
        set
        {
            if (value < JJNumberComparison.Exact || value > JJNumberComparison.Numeric)
                throw new ArgumentOutOfRangeException(nameof(NumberComparison));
            ValueMode = (ValueMode & ~JJValueMatchingMode.Numeric)
                | (value == JJNumberComparison.Numeric ? JJValueMatchingMode.Numeric : JJValueMatchingMode.None);
        }
    }

    private JJMatchLimits _limits = new JJMatchLimits();

    /// <summary>Advanced resource limits. Legacy top-level limit properties share this same state.</summary>
    public JJMatchLimits Limits
    {
        get => _limits;
        set => _limits = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Array policy with explicit names. Shares state with legacy ArrayMode.</summary>
    public JJArrayComparison ArrayComparison
    {
        get => ArrayMode == JJArrayMatchingMode.Strict ? JJArrayComparison.Unordered : JJArrayComparison.DistinctShapes;
        set
        {
            if (value < JJArrayComparison.Unordered || value > JJArrayComparison.DistinctShapes)
                throw new ArgumentOutOfRangeException(nameof(ArrayComparison));
            ArrayMode = value == JJArrayComparison.Unordered ? JJArrayMatchingMode.Strict : JJArrayMatchingMode.StructureOnly;
        }
    }

    /// <summary>Array policy. The public API defaults to count-preserving comparison with values; element order is never significant.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public JJArrayMatchingMode ArrayMode { get; set; } = JJArrayMatchingMode.Strict;

    /// <summary>
    /// Primitive value rules, combined by OR, each accepting further pairs on top of identical values, which always
    /// match. Defaults to None: identical values only.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public JJValueMatchingMode ValueMode { get; set; } = JJValueMatchingMode.None;

    /// <summary>Property name rule, accepting further pairs on top of identical names, which always pair. Defaults to None: identical names only.</summary>
    public JJPropertyNameMatchingMode PropertyNameMode { get; set; } = JJPropertyNameMatchingMode.None;

    /// <summary>
    /// Text of your choice that marks a property to leave out, such as <c>"_"</c>, <c>"*"</c> or <c>"ignore_"</c>; nothing is
    /// built in. A property whose name starts with it, compared
    /// character by character before any name rule, is removed from both documents at every level, including inside
    /// a decoded stringified document, so it is never compared, reported or counted in the score. Defaults to null:
    /// every property is kept. The empty string is rejected, since it would remove every property.
    /// </summary>
    public string? IgnoredPropertyPrefix { get; set; }

    /// <summary>
    /// Least similarity the Fuzzy value rule accepts, from 0 to 1: one minus the edits allowed per character of the
    /// longer text. Defaults to 0.8, one edit for every five characters; 1 allows no edit and 0 accepts any two strings.
    /// </summary>
    public double FuzzyThreshold { get; set; } = ValueMatcher.DefaultFuzzyThreshold;

    private JJScoringSettings _scoring = new JJScoringSettings();

    /// <summary>
    /// How the score is computed. Never null: a new settings object holds a configuration selecting
    /// <see cref="JJScoringMode.Standard"/> scoring with the default weights, and assigning null throws. Set
    /// <see cref="JJScoringSettings.Mode"/> to <see cref="JJScoringMode.Weighted"/> and adjust the weights to make
    /// some kinds of difference lower the score less than others.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public JJScoringSettings Scoring
    {
        get => _scoring;
        set => _scoring = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Maximum container depth of each parsed document, at least 1, with no upper bound. Defaults to 512. The comparison
    /// recurses once per level, so a very deep document can overflow the stack when the depth allows it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public int MaxDepth { get => Limits.MaxDepth; set => Limits.MaxDepth = value; }

    /// <summary>Maximum UTF-16 length of each input, including any decoded document. Defaults to 1,000,000.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public int MaxInputLength { get => Limits.MaxInputLength; set => Limits.MaxInputLength = value; }

    /// <summary>Maximum total JSON nodes across both inputs, from 1 to 1,000,000. Defaults to 100,000.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public int MaxNodes { get => Limits.MaxNodes; set => Limits.MaxNodes = value; }

    /// <summary>Maximum occurrences per side of an array or duplicate-property assignment, from 1 to 256. Defaults to 128.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public int MaxAssignmentSize { get => Limits.MaxAssignmentSize; set => Limits.MaxAssignmentSize = value; }

    /// <summary>Comparison work budget, including speculative pairs, signatures and value-distance work. Defaults to 10,000,000. Not a time limit.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public long MaxWork { get => Limits.MaxWork; set => Limits.MaxWork = value; }

    /// <summary>
    /// Reads settings from a JSON object, such as the content of a <c>JJSetting.json</c> file. Each property is
    /// named like the property here, ignoring case, and an omitted property keeps its default. Modes are written as
    /// member names (<c>"StructureOnly"</c>); value rules are joined by <c>|</c> or <c>,</c> (<c>"Normalized | Numeric"</c>).
    /// Limits are integers, <see cref="FuzzyThreshold"/> a number and <see cref="IgnoredPropertyPrefix"/> a string or
    /// null. <see cref="Scoring"/> is a nested object whose
    /// properties are named like those of <see cref="JJScoringSettings"/>, with the mode as a member name and the
    /// weights as integers. The result is validated like settings passed to <see cref="JJMatcher.Match"/>.
    /// </summary>
    /// <param name="json">The JSON text of the settings object.</param>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    /// <exception cref="JsonException">The text is not valid JSON.</exception>
    /// <exception cref="ArgumentException">The root is not an object, a name is unknown, or a value has the wrong type or is out of range.</exception>
    public static JJMatchSettings FromJson(string json)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Settings must be a JSON object.", nameof(json));
        var names = new HashSet<string>(document.RootElement.EnumerateObject().Select(p => p.Name), System.StringComparer.OrdinalIgnoreCase);
        if (names.Contains(nameof(ArrayMode)) && names.Contains(nameof(ArrayComparison)))
            throw new ArgumentException("Use ArrayComparison or legacy ArrayMode, not both.", nameof(json));
        if (names.Contains(nameof(ValueMode)) && (names.Contains(nameof(StringComparison)) || names.Contains(nameof(NumberComparison))))
            throw new ArgumentException("Use StringComparison and NumberComparison, or legacy ValueMode, not both.", nameof(json));
        if (names.Contains(nameof(Limits)) && new[] { nameof(MaxDepth), nameof(MaxInputLength), nameof(MaxNodes), nameof(MaxAssignmentSize), nameof(MaxWork) }.Any(names.Contains))
            throw new ArgumentException("Use Limits or legacy top-level limits, not both.", nameof(json));
        var settings = new JJMatchSettings();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (Is(property, nameof(Mode))) settings.Mode = Member<JJComparisonMode>(property);
            else if (Is(property, nameof(StringComparison))) settings.StringComparison = Member<JJStringComparison>(property);
            else if (Is(property, nameof(NumberComparison))) settings.NumberComparison = Member<JJNumberComparison>(property);
            else if (Is(property, nameof(IncludeStructuralScore))) settings.IncludeStructuralScore = Boolean(property);
            else if (Is(property, nameof(DecodeStringifiedJson))) settings.DecodeStringifiedJson = Boolean(property);
            else if (Is(property, nameof(Limits))) settings.Limits = LimitsObject(property);
            else if (Is(property, nameof(ArrayComparison))) settings.ArrayComparison = Member<JJArrayComparison>(property);
            else if (Is(property, nameof(ArrayMode))) settings.ArrayMode = Member<JJArrayMatchingMode>(property);
            else if (Is(property, nameof(ValueMode))) settings.ValueMode = Member<JJValueMatchingMode>(property);
            else if (Is(property, nameof(PropertyNameMode))) settings.PropertyNameMode = Member<JJPropertyNameMatchingMode>(property);
            else if (Is(property, nameof(IgnoredPropertyPrefix))) settings.IgnoredPropertyPrefix = Text(property);
            else if (Is(property, nameof(MaxDepth))) settings.MaxDepth = Int32(property);
            else if (Is(property, nameof(MaxInputLength))) settings.MaxInputLength = Int32(property);
            else if (Is(property, nameof(MaxNodes))) settings.MaxNodes = Int32(property);
            else if (Is(property, nameof(MaxAssignmentSize))) settings.MaxAssignmentSize = Int32(property);
            else if (Is(property, nameof(MaxWork))) settings.MaxWork = Int64(property);
            else if (Is(property, nameof(FuzzyThreshold))) settings.FuzzyThreshold = Double(property);
            else if (Is(property, nameof(Scoring))) settings.Scoring = ScoringObject(property);
            else throw new ArgumentException($"Unknown setting '{property.Name}'.", nameof(json));
        }
        return settings.Snapshot();

        static bool Boolean(JsonProperty property) => property.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException($"'{property.Name}' must be a boolean.", nameof(json))
        };

        static JJMatchLimits LimitsObject(JsonProperty property)
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"'{property.Name}' must be an object.", nameof(json));
            var limits = new JJMatchLimits();
            foreach (var setting in property.Value.EnumerateObject())
            {
                if (Is(setting, nameof(MaxDepth))) limits.MaxDepth = Int32(setting);
                else if (Is(setting, nameof(MaxInputLength))) limits.MaxInputLength = Int32(setting);
                else if (Is(setting, nameof(MaxNodes))) limits.MaxNodes = Int32(setting);
                else if (Is(setting, nameof(MaxAssignmentSize))) limits.MaxAssignmentSize = Int32(setting);
                else if (Is(setting, nameof(MaxWork))) limits.MaxWork = Int64(setting);
                else throw new ArgumentException($"Unknown setting 'Limits.{setting.Name}'.", nameof(json));
            }
            return limits;
        }

        static JJScoringSettings ScoringObject(JsonProperty property)
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"'{property.Name}' must be an object.", nameof(json));
            var scoring = new JJScoringSettings();
            foreach (var setting in property.Value.EnumerateObject())
            {
                if (Is(setting, nameof(JJScoringSettings.Mode))) scoring.Mode = Member<JJScoringMode>(setting);
                else if (Is(setting, nameof(JJScoringSettings.MissingWeight))) scoring.MissingWeight = Int32(setting);
                else if (Is(setting, nameof(JJScoringSettings.AddedWeight))) scoring.AddedWeight = Int32(setting);
                else if (Is(setting, nameof(JJScoringSettings.TypeMismatchWeight))) scoring.TypeMismatchWeight = Int32(setting);
                else if (Is(setting, nameof(JJScoringSettings.ValueMismatchWeight))) scoring.ValueMismatchWeight = Int32(setting);
                else if (Is(setting, nameof(JJScoringSettings.PropertyNameCaseMismatchWeight))) scoring.PropertyNameCaseMismatchWeight = Int32(setting);
                else if (Is(setting, nameof(JJScoringSettings.ValueCaseMismatchWeight))) scoring.ValueCaseMismatchWeight = Int32(setting);
                else throw new ArgumentException($"Unknown setting '{property.Name}.{setting.Name}'.", nameof(json));
            }
            return scoring;
        }

        static string? Text(JsonProperty property) => property.Value.ValueKind switch
        {
            JsonValueKind.String => property.Value.GetString(),
            JsonValueKind.Null => null,
            _ => throw new ArgumentException($"'{property.Name}' must be a string or null.", nameof(json))
        };

        static double Double(JsonProperty property) =>
            property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var value)
                ? value
                : throw new ArgumentException($"'{property.Name}' must be a number.", nameof(json));

        static bool Is(JsonProperty property, string name) =>
            string.Equals(property.Name, name, System.StringComparison.OrdinalIgnoreCase);

        static T Member<T>(JsonProperty property) where T : struct, Enum
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && Enum.TryParse<T>(property.Value.GetString()!.Replace('|', ','), true, out var value))
                return value;
            throw new ArgumentException($"'{property.Name}' must name a {typeof(T).Name} member.", nameof(json));
        }

        static int Int32(JsonProperty property) =>
            property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value)
                ? value
                : throw new ArgumentException($"'{property.Name}' must be an integer.", nameof(json));

        static long Int64(JsonProperty property) =>
            property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var value)
                ? value
                : throw new ArgumentException($"'{property.Name}' must be an integer.", nameof(json));
    }

    internal JJMatchSettings Snapshot()
    {
        var copy = (JJMatchSettings)MemberwiseClone();
        // The scoring configuration is a reference: it is copied and validated on its own, so a later change to the
        // caller's object cannot reach a running comparison.
        copy._scoring = _scoring.Snapshot();
        copy._limits = _limits.Snapshot();
        if (copy.Mode < JJComparisonMode.Full || copy.Mode > JJComparisonMode.StructureOnly)
            throw new ArgumentOutOfRangeException(nameof(Mode));
        if (copy.ArrayMode < JJArrayMatchingMode.StructureOnly || copy.ArrayMode > JJArrayMatchingMode.Strict)
            throw new ArgumentOutOfRangeException(nameof(ArrayMode));
        const JJValueMatchingMode known = JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric;
        if ((copy.ValueMode & ~known) != 0) throw new ArgumentOutOfRangeException(nameof(ValueMode));
        if (copy.PropertyNameMode < JJPropertyNameMatchingMode.None || copy.PropertyNameMode > JJPropertyNameMatchingMode.Normalized)
            throw new ArgumentOutOfRangeException(nameof(PropertyNameMode));
        if (copy.IgnoredPropertyPrefix is { Length: 0 }) throw new ArgumentOutOfRangeException(nameof(IgnoredPropertyPrefix));
        // Written as a double negative so that NaN is rejected too.
        if (!(copy.FuzzyThreshold >= 0 && copy.FuzzyThreshold <= 1)) throw new ArgumentOutOfRangeException(nameof(FuzzyThreshold));
        if (copy.MaxDepth < 1) throw new ArgumentOutOfRangeException(nameof(MaxDepth));
        if (copy.MaxInputLength < 1) throw new ArgumentOutOfRangeException(nameof(MaxInputLength));
        if (copy.MaxNodes < 1 || copy.MaxNodes > 1_000_000) throw new ArgumentOutOfRangeException(nameof(MaxNodes));
        if (copy.MaxAssignmentSize < 1 || copy.MaxAssignmentSize > 256) throw new ArgumentOutOfRangeException(nameof(MaxAssignmentSize));
        if (copy.MaxWork < 1) throw new ArgumentOutOfRangeException(nameof(MaxWork));
        return copy;
    }
}
