using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Serialization;

internal static class LuaEntryTableJsonNormalizer
{
    public static JsonNode? Normalize(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => NormalizeObject(element),
        JsonValueKind.Array => NormalizeArray(element),
        JsonValueKind.Null => null,
        _ => JsonNode.Parse(element.GetRawText())
    };

    private static JsonObject NormalizeObject(JsonElement element)
    {
        JsonObject result = [];
        foreach (JsonProperty property in element.EnumerateObject())
            result[property.Name] = Normalize(property.Value);
        return result;
    }

    private static JsonNode NormalizeArray(JsonElement element)
    {
        JsonElement[] entries = [.. element.EnumerateArray()];
        if (entries.Length > 0 && entries.All(IsKeyValueEntry))
        {
            JsonObject result = [];
            foreach (JsonElement entry in entries)
            {
                string key = entry.GetProperty("KEY").GetString() ?? string.Empty;
                if (key.Length > 0)
                    result[key] = Normalize(entry.GetProperty("VAL"));
            }

            return result;
        }

        JsonArray array = [];
        bool isValueEntryArray = entries.Length > 0 && entries.All(IsValueEntry);
        foreach (JsonElement entry in entries)
            array.Add(Normalize(isValueEntryArray ? entry.GetProperty("VAL") : entry));
        return array;
    }

    private static bool IsKeyValueEntry(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("KEY", out _) &&
        element.TryGetProperty("VAL", out _);

    private static bool IsValueEntry(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("VAL", out _) &&
        element.EnumerateObject().All(property => property.NameEquals("VAL"));
}

internal static class ArgbColorJson
{
    public static bool TryReadHex(JsonElement element, out byte a, out byte r, out byte g, out byte b)
    {
        a = r = g = b = 0;
        string? text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        if (text is null || !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.Length != 10 ||
            !uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            return false;
        }

        a = (byte)(argb >> 24);
        r = (byte)(argb >> 16);
        g = (byte)(argb >> 8);
        b = (byte)argb;
        return true;
    }
}

internal sealed class ArgbFloatArrayJsonConverter : JsonConverter<float[]>
{
    public override float[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement;
        if (ArgbColorJson.TryReadHex(element, out byte a, out byte r, out byte g, out byte b))
            return [a / 255f, r / 255f, g / 255f, b / 255f];

        if (element.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected an ARGB string or numeric array.");

        return [.. element.EnumerateArray().Select(component => component.GetSingle())];
    }

    public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (float component in value)
            writer.WriteNumberValue(component);
        writer.WriteEndArray();
    }
}

internal sealed class ArgbIntArrayJsonConverter : JsonConverter<int[]>
{
    public override int[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement;
        if (ArgbColorJson.TryReadHex(element, out byte a, out byte r, out byte g, out byte b))
            return [a, r, g, b];

        if (element.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected an ARGB string or numeric array.");

        return [.. element.EnumerateArray().Select(component => component.GetInt32())];
    }

    public override void Write(Utf8JsonWriter writer, int[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (int component in value)
            writer.WriteNumberValue(component);
        writer.WriteEndArray();
    }
}