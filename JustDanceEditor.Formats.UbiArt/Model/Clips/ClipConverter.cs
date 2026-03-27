using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Model.Clips;

public class ClipConverter : JsonConverter<Clip>
{
    public override Clip Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("__class", out JsonElement classNameElement))
            throw new JsonException("Missing __class property.");

        string className = classNameElement.GetString() ?? throw new JsonException("Clip __class property was null.");
        Type? type = Type.GetType($"JustDanceEditor.Formats.UbiArt.Model.Clips.{className}");

        return type == null
            ? throw new JsonException($"Unknown clip type: {className}")
            : JsonSerializer.Deserialize(root.GetRawText(), type, options) as Clip ?? throw new JsonException($"Failed to deserialize clip type: {className}");
    }

    public override void Write(Utf8JsonWriter writer, Clip value, JsonSerializerOptions? options = null)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}