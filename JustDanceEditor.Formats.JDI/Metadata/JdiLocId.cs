using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Metadata;

[JsonConverter(typeof(JdiLocIdJsonConverter))]
public readonly record struct JdiLocId
{
    public static readonly JdiLocId Zero = new("0");
    private readonly string? _value;

    public JdiLocId(string value)
    {
        _value = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
    }

    public string Value => _value ?? "0";

    public bool IsNumeric => long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static implicit operator JdiLocId(string value) => new(value);
    public static implicit operator string(JdiLocId value) => value.Value;

    public override string ToString() => Value;
}

public sealed class JdiLocIdJsonConverter : JsonConverter<JdiLocId>
{
    public override JdiLocId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => new JdiLocId(reader.GetString() ?? throw new JsonException("Expected a string value.")),
            JsonTokenType.Number => new JdiLocId(reader.GetInt64().ToString(CultureInfo.InvariantCulture)),
            _ => throw new JsonException("Expected string or number value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, JdiLocId value, JsonSerializerOptions options)
    {
        if (long.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numericValue))
            writer.WriteNumberValue(numericValue);
        else
            writer.WriteStringValue(value.Value);
    }
}
