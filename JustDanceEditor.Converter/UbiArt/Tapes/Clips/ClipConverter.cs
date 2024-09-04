using System.Text.Json.Serialization;
using System.Text.Json;

namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public class ClipConverter : JsonConverter<IClip>
{
    public override IClip Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("__class", out JsonElement classNameElement))
        {
            throw new JsonException("Missing __class property.");
        }

        string className = classNameElement.GetString()!;
        Type? type = Type.GetType($"JustDanceEditor.Converter.UbiArt.Tapes.Clips.{className}");

        return type == null
            ? throw new JsonException($"Unknown clip type: {className}")
            : (IClip)JsonSerializer.Deserialize(root.GetRawText(), type, options)!;
    }

    public override void Write(Utf8JsonWriter writer, IClip value, JsonSerializerOptions? options = null)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}