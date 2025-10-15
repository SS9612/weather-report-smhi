using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace weather_report_smhi.Infrastructure.Seralization;

/// <summary>
/// Allows System.Text.Json to read double? values that may arrive as numbers or strings
/// (e.g., "0.0", "0,0", "NaN", ""). Non-numeric parses → null.
/// </summary>
public sealed class FlexibleDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Numeric → read directly
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetDouble(out var d)) return d;
            return null;
        }

        // Null → null
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        // String → try multiple cultures/variants
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;

            // Common non-numeric markers
            if (string.Equals(s, "NaN", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(s, "-", StringComparison.OrdinalIgnoreCase)) return null;

            // Try invariant
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d))
                return d;

            // Try Swedish culture (comma decimal)
            var sv = CultureInfo.GetCultureInfo("sv-SE");
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, sv, out d))
                return d;

            // Fallback: replace comma with dot and try again
            s = s.Replace(',', '.');
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out d))
                return d;

            return null;
        }

        // If it's an object/array/bool, consume it and return null to avoid throwing.
        using var _ = JsonDocument.ParseValue(ref reader);
        return null;
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
