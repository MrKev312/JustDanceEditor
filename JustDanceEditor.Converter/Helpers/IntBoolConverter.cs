using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Converter.Helpers;

public class IntBoolConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int? number = reader.TokenType switch
        {
            JsonTokenType.False => 0,
            JsonTokenType.True => 1,
            JsonTokenType.Number when reader.TryGetInt32(out int intValue) => intValue,
            JsonTokenType.Number when reader.TryGetDouble(out double doubleValue) => (int)doubleValue,
            _ => null
        };

        if (number.HasValue)
            return number.Value;

        if (reader.TokenType == JsonTokenType.String)
            if (int.TryParse(reader.GetString(), out int result))
                return result;
            else
                throw new JsonException("Unable to convert string to int");
        else
            throw new JsonException("Unable to convert value to int");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
