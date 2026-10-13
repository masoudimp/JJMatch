using System.Text.Json;

namespace JJMatch;

/// <summary>Optional comparison rules and resource limits. Configure before calling <see cref="JJMatcher.Match"/>, or load them with <see cref="FromJson"/>.</summary>
public sealed class JJMatchSettings
{
    /// <summary>Array policy. The public API defaults to ordered, count-preserving comparison.</summary>
    public JJArrayMatchingMode ArrayMode { get; set; } = JJArrayMatchingMode.Strict;

    /// <summary>
    /// Primitive value rules, combined by OR, each accepting further pairs on top of identical values, which always
    /// match. Defaults to None: identical values only.
    /// </summary>
    public JJValueMatchingMode ValueMode { get; set; } = JJValueMatchingMode.None;

    /// <summary>Property name rule. Defaults to Exact, so names are compared character by character.</summary>
    public JJPropertyNameMatchingMode PropertyNameMode { get; set; } = JJPropertyNameMatchingMode.Exact;

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
    /// Maximum container depth of each parsed document, at least 1, with no upper bound. Defaults to 64. The comparison
    /// recurses once per level, so a very deep document can overflow the stack when the depth allows it.
    /// </summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>Maximum UTF-16 length of each input, including any decoded document. Defaults to 1,000,000.</summary>
    public int MaxInputLength { get; set; } = 1_000_000;

    /// <summary>Maximum total JSON nodes across both inputs, from 1 to 1,000,000. Defaults to 100,000.</summary>
    public int MaxNodes { get; set; } = 100_000;

    /// <summary>Maximum occurrences per side of an unordered or duplicate-property assignment, from 1 to 256. Defaults to 128.</summary>
    public int MaxAssignmentSize { get; set; } = 128;

    /// <summary>Comparison work budget, including speculative pairs, signatures and value-distance work. Defaults to 10,000,000. Not a time limit.</summary>
    public long MaxWork { get; set; } = 10_000_000;

    /// <summary>
    /// Reads settings from a JSON object, such as the content of a <c>JJSetting.json</c> file. Each property is
    /// named like the property here, ignoring case, and an omitted property keeps its default. Modes are written as
    /// member names (<c>"Unordered"</c>); value rules are joined by <c>|</c> or <c>,</c> (<c>"Normalized | Numeric"</c>).
    /// Limits are integers and <see cref="FuzzyThreshold"/> a number. <see cref="Scoring"/> is a nested object whose
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
        var settings = new JJMatchSettings();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (Is(property, nameof(ArrayMode))) settings.ArrayMode = Member<JJArrayMatchingMode>(property);
            else if (Is(property, nameof(ValueMode))) settings.ValueMode = Member<JJValueMatchingMode>(property);
            else if (Is(property, nameof(PropertyNameMode))) settings.PropertyNameMode = Member<JJPropertyNameMatchingMode>(property);
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
                else throw new ArgumentException($"Unknown setting '{property.Name}.{setting.Name}'.", nameof(json));
            }
            return scoring;
        }

        static double Double(JsonProperty property) =>
            property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var value)
                ? value
                : throw new ArgumentException($"'{property.Name}' must be a number.", nameof(json));

        static bool Is(JsonProperty property, string name) =>
            string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase);

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
        if (copy.ArrayMode < JJArrayMatchingMode.StructureOnly || copy.ArrayMode > JJArrayMatchingMode.Strict)
            throw new ArgumentOutOfRangeException(nameof(ArrayMode));
        const JJValueMatchingMode known = JJValueMatchingMode.Normalized | JJValueMatchingMode.Fuzzy | JJValueMatchingMode.Numeric;
        if ((copy.ValueMode & ~known) != 0) throw new ArgumentOutOfRangeException(nameof(ValueMode));
        if (copy.PropertyNameMode < JJPropertyNameMatchingMode.Exact || copy.PropertyNameMode > JJPropertyNameMatchingMode.Normalized)
            throw new ArgumentOutOfRangeException(nameof(PropertyNameMode));
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
