using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Numerics;
using System.Text;
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

        return className switch
        {
            "GoldEffectClip" => JsonSerializer.Deserialize<GoldEffectClip>(root.GetRawText(), options)!,
            "HideUserInterfaceClip" => JsonSerializer.Deserialize<HideUserInterfaceClip>(root.GetRawText(), options)!,
            "KaraokeClip" => JsonSerializer.Deserialize<KaraokeClip>(root.GetRawText(), options)!,
            "MotionClip" => JsonSerializer.Deserialize<MotionClip>(root.GetRawText(), options)!,
            "PictogramClip" => JsonSerializer.Deserialize<PictogramClip>(root.GetRawText(), options)!,
            "SoundSetClip" => JsonSerializer.Deserialize<SoundSetClip>(root.GetRawText(), options)!,
            "VibrationClip" => JsonSerializer.Deserialize<VibrationClip>(root.GetRawText(), options)!,

            _ => throw new JsonException($"Unknown clip type: {className}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, IClip value, JsonSerializerOptions? options = null)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}