using System.IO;
using System.Text;
using System.Text.Json;

namespace JJMatch;

public sealed partial class JJMatchSettings
{
    /// <summary>
    /// Exports a minimal configuration using the simple property names. Defaults are omitted.
    /// Inactive fuzzy thresholds and scoring weights are omitted unless includeInactiveSettings is true.
    /// Equivalent legacy value flags are expressed as one string rule and one number rule.
    /// </summary>
    public string ToJson(bool includeInactiveSettings = false)
    {
        var config = Snapshot();
        var defaults = new JJMatchSettings();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            if (config.Mode != defaults.Mode) writer.WriteString(nameof(Mode), config.Mode.ToString());
            if (config.ArrayMode != defaults.ArrayMode) writer.WriteString(nameof(ArrayComparison), nameof(JJArrayComparison.DistinctShapes));
            if (config.StringComparison != defaults.StringComparison) writer.WriteString(nameof(StringComparison), config.StringComparison.ToString());
            if (config.NumberComparison != defaults.NumberComparison) writer.WriteString(nameof(NumberComparison), config.NumberComparison.ToString());
            if (config.PropertyNameMode != defaults.PropertyNameMode) writer.WriteString(nameof(PropertyNameMode), config.PropertyNameMode.ToString());
            if (config.IgnoredPropertyPrefix is not null) writer.WriteString(nameof(IgnoredPropertyPrefix), config.IgnoredPropertyPrefix);
            if (!config.IncludeStructuralScore) writer.WriteBoolean(nameof(IncludeStructuralScore), false);
            if (!config.DecodeStringifiedJson) writer.WriteBoolean(nameof(DecodeStringifiedJson), false);
            if (includeInactiveSettings || config.StringComparison == JJStringComparison.Fuzzy)
                Number(nameof(FuzzyThreshold), config.FuzzyThreshold, defaults.FuzzyThreshold);

            if (config.Scoring.Mode != JJScoringMode.Standard || includeInactiveSettings)
            {
                var scoring = config.Scoring;
                var original = defaults.Scoring;
                // Buffer entries first so a default-only section is not emitted.
                var weights = new (string Name, int Value, int Default)[]
                {
                    (nameof(scoring.MissingWeight), scoring.MissingWeight, original.MissingWeight),
                    (nameof(scoring.AddedWeight), scoring.AddedWeight, original.AddedWeight),
                    (nameof(scoring.TypeMismatchWeight), scoring.TypeMismatchWeight, original.TypeMismatchWeight),
                    (nameof(scoring.ValueMismatchWeight), scoring.ValueMismatchWeight, original.ValueMismatchWeight),
                    (nameof(scoring.PropertyNameCaseMismatchWeight), scoring.PropertyNameCaseMismatchWeight, original.PropertyNameCaseMismatchWeight),
                    (nameof(scoring.ValueCaseMismatchWeight), scoring.ValueCaseMismatchWeight, original.ValueCaseMismatchWeight)
                };
                if (scoring.Mode != original.Mode || weights.Any(w => w.Value != w.Default))
                {
                    writer.WriteStartObject(nameof(Scoring));
                    if (scoring.Mode != original.Mode) writer.WriteString(nameof(scoring.Mode), scoring.Mode.ToString());
                    foreach (var weight in weights) Number(weight.Name, weight.Value, weight.Default);
                    writer.WriteEndObject();
                }
            }

            if (config.MaxDepth != defaults.MaxDepth || config.MaxInputLength != defaults.MaxInputLength
                || config.MaxNodes != defaults.MaxNodes || config.MaxAssignmentSize != defaults.MaxAssignmentSize
                || config.MaxWork != defaults.MaxWork)
            {
                writer.WriteStartObject(nameof(Limits));
                Number(nameof(MaxDepth), config.MaxDepth, defaults.MaxDepth);
                Number(nameof(MaxInputLength), config.MaxInputLength, defaults.MaxInputLength);
                Number(nameof(MaxNodes), config.MaxNodes, defaults.MaxNodes);
                Number(nameof(MaxAssignmentSize), config.MaxAssignmentSize, defaults.MaxAssignmentSize);
                if (config.MaxWork != defaults.MaxWork) writer.WriteNumber(nameof(MaxWork), config.MaxWork);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            void Number(string name, double value, double original)
            {
                if (value != original) writer.WriteNumber(name, value);
            }
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
