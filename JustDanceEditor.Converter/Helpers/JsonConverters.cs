using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Converter.Helpers;

public class IntFlexibleJsonConverter : JsonConverter<int>
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
        {
            string str = reader.GetString()!;

            if (int.TryParse(str, out int result))
                return result;
            else
                throw new JsonException("Unable to convert string to int: " + str);
        }
        else
            throw new JsonException("Unable to convert value to int");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

public class BoolFlexibleJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.False => false,
            JsonTokenType.True => true,
            JsonTokenType.Number when reader.TryGetInt32(out int intValue) => intValue != 0,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out bool boolValue) => boolValue,
            _ => throw new JsonException("Unable to convert value to bool")
        };
    }
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}