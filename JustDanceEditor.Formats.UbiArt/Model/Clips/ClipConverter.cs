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
            ? CreateUnknownClip(root, className)
            : JsonSerializer.Deserialize(root.GetRawText(), type, options) as Clip ?? throw new JsonException($"Failed to deserialize clip type: {className}");
    }

    public override void Write(Utf8JsonWriter writer, Clip value, JsonSerializerOptions? options = null)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }

    private static UnknownClip CreateUnknownClip(JsonElement root, string className)
    {
        UnknownClip clip = new()
        {
            OriginalClass = className
        };

        if (root.TryGetProperty("Id", out JsonElement id) && id.TryGetInt64(out long idValue))
            clip.Id = idValue;
        if (root.TryGetProperty("TrackId", out JsonElement trackId) && trackId.TryGetInt64(out long trackIdValue))
            clip.TrackId = trackIdValue;
        if (root.TryGetProperty("IsActive", out JsonElement isActive) && isActive.TryGetInt32(out int isActiveValue))
            clip.IsActive = isActiveValue;
        if (root.TryGetProperty("StartTime", out JsonElement startTime) && startTime.TryGetInt32(out int startTimeValue))
            clip.StartTime = startTimeValue;
        if (root.TryGetProperty("Duration", out JsonElement duration) && duration.TryGetInt32(out int durationValue))
            clip.Duration = durationValue;

        return clip;
    }
}
