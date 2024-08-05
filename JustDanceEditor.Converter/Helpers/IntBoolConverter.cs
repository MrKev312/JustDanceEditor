using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Converter.Helpers;

public class IntBoolConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.False => 0,
            JsonTokenType.True => 1,
            JsonTokenType.Number when reader.TryGetInt32(out int intValue) => intValue,
            _ => throw new JsonException("Unexpected token type")
        };
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
