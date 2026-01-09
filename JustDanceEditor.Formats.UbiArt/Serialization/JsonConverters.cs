using JustDanceEditor.Formats.UbiArt.Tapes;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Serialization;

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

public class FloatArrayFlexibleJsonConverter : JsonConverter<float[]>
{
    public override float[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            List<float> floats = [];
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;
                else if (reader.TokenType == JsonTokenType.Number)
                {
                    if (reader.TryGetSingle(out float floatValue))
                        floats.Add(floatValue);
                    else if (reader.TryGetDouble(out double doubleValue))
                        floats.Add((float)doubleValue);
                    else
                        throw new JsonException("Unable to convert number to float");
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    string str = reader.GetString()!;
                    if (float.TryParse(str, out float floatValue))
                        floats.Add(floatValue);
                    else
                        throw new JsonException($"Unable to convert string '{str}' to float");
                }
            }

            return [.. floats];
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            // Handle string representation of float array
            string str = reader.GetString()!;

            // Try hex color format (0xAARRGGBB or 0xRRGGBBAA)
            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && str.Length == 10)
            {
                string hex = str[2..];
                try
                {
                    uint argb = uint.Parse(hex, System.Globalization.NumberStyles.HexNumber);

                    // Parse as ARGB (most common for UbiArt)
                    byte a = (byte)((argb >> 24) & 0xFF);
                    byte r = (byte)((argb >> 16) & 0xFF);
                    byte g = (byte)((argb >> 8) & 0xFF);
                    byte b = (byte)(argb & 0xFF);

                    // Normalize to 0-1 range
                    return [r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f];
                }
                catch
                {
                    throw new JsonException($"Unable to parse hex color '{str}'");
                }
            }

            // Try comma-separated values
            if (str.Contains(','))
            {
                string[] parts = str.Split(',', StringSplitOptions.RemoveEmptyEntries);
                List<float> floats = [];
                foreach (string part in parts)
                {
                    if (float.TryParse(part.Trim(), out float value))
                        floats.Add(value);
                    else
                        throw new JsonException($"Unable to convert string part '{part}' to float");
                }

                return [.. floats];
            }

            // Try space-separated values
            if (str.Contains(' '))
            {
                string[] parts = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                List<float> floats = [];
                foreach (string part in parts)
                {
                    if (float.TryParse(part.Trim(), out float value))
                        floats.Add(value);
                    else
                        throw new JsonException($"Unable to convert string part '{part}' to float");
                }

                return [.. floats];
            }

            // If it's a single float value
            if (float.TryParse(str, out float singleValue))
                return [singleValue];

            throw new JsonException($"Unable to convert string '{str}' to float[]");
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Handle Lua table format: { 1: value1, 2: value2, ...} or { r: red, g: green, b: blue, a: alpha }
            Dictionary<string, float> values = [];
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string key = reader.GetString()!;
                    if (reader.Read())
                    {
                        float value = 0f;
                        if (reader.TokenType == JsonTokenType.Number)
                        {
                            if (reader.TryGetSingle(out float floatValue))
                                value = floatValue;
                            else if (reader.TryGetDouble(out double doubleValue))
                                value = (float)doubleValue;
                        }
                        else if (reader.TokenType == JsonTokenType.String && float.TryParse(reader.GetString(), out float parsed))
                            value = parsed;

                        values[key] = value;
                    }
                }
            }

            // Try numeric keys first (1-indexed Lua arrays)
            List<float> result = [];
            for (int i = 1; i <= values.Count; i++)
            {
                if (values.TryGetValue(i.ToString(), out float val))
                    result.Add(val);
                else
                    break;
            }

            if (result.Count > 0)
                return [.. result];

            // Fall back to RGBA order if numeric keys don't exist
            float r = 0, g = 0, b = 0, a = 1;
            if (values.TryGetValue("r", out float rv) || values.TryGetValue("R", out rv))
                r = rv;
            if (values.TryGetValue("g", out float gv) || values.TryGetValue("G", out gv))
                g = gv;
            if (values.TryGetValue("b", out float bv) || values.TryGetValue("B", out bv))
                b = bv;
            if (values.TryGetValue("a", out float av) || values.TryGetValue("A", out av))
                a = av;

            return [r, g, b, a];
        }
        else if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        else
        {
            throw new JsonException($"Unable to convert token type {reader.TokenType} to float[]");
        }
    }

    public override void Write(Utf8JsonWriter writer, float[]? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (float f in value)
            writer.WriteNumberValue(f);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Converter for Structure that handles the MusicTrackStructure wrapper.
/// Handles: { MusicTrackStructure: {...} } → Structure
/// </summary>
public class StructureJsonConverter : JsonConverter<Structure>
{
    public override Structure? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        // Check if wrapped in MusicTrackStructure
        if (root.TryGetProperty("MusicTrackStructure", out JsonElement innerStructure))
        {
            return DeserializeStructure(innerStructure, options);
        }

        // Not wrapped, deserialize directly
        return DeserializeStructure(root, options);
    }

    private static Structure DeserializeStructure(JsonElement element, JsonSerializerOptions options)
    {
        Structure structure = new();

        if (element.TryGetProperty("startBeat", out JsonElement sb))
            structure.StartBeat = sb.GetInt32();
        if (element.TryGetProperty("endBeat", out JsonElement eb))
            structure.EndBeat = eb.GetInt32();
        if (element.TryGetProperty("videoStartTime", out JsonElement vst))
            structure.VideoStartTime = vst.GetSingle();
        if (element.TryGetProperty("previewEntry", out JsonElement pe))
            structure.PreviewEntry = pe.GetInt32();
        if (element.TryGetProperty("previewLoopStart", out JsonElement pls))
            structure.PreviewLoopStart = pls.GetInt32();
        if (element.TryGetProperty("previewLoopEnd", out JsonElement ple))
            structure.PreviewLoopEnd = ple.GetInt32();
        if (element.TryGetProperty("previewDuration", out JsonElement pd))
            structure.PreviewDuration = pd.GetInt32();

        // Parse markers: [{VAL: n}, ...] → int[]
        if (element.TryGetProperty("markers", out JsonElement markers))
        {
            List<int> markerList = [];
            foreach (JsonElement marker in markers.EnumerateArray())
            {
                if (marker.TryGetProperty("VAL", out JsonElement val))
                    markerList.Add(val.GetInt32());
            }

            structure.Markers = [.. markerList];
        }

        // Parse signatures: [{MusicSignature: {...}}, ...] → Signature[]
        if (element.TryGetProperty("signatures", out JsonElement signatures))
        {
            List<Signature> sigList = [];
            foreach (JsonElement sig in signatures.EnumerateArray())
            {
                JsonElement inner = sig.TryGetProperty("MusicSignature", out JsonElement ms) ? ms : sig;
                Signature signature = new();
                if (inner.TryGetProperty("beats", out JsonElement beats))
                    signature.Beats = beats.GetInt32();
                if (inner.TryGetProperty("marker", out JsonElement marker))
                    signature.Marker = marker.GetSingle();
                if (inner.TryGetProperty("comment", out JsonElement comment))
                    signature.Comment = comment.GetString() ?? "";
                sigList.Add(signature);
            }

            structure.Signatures = [.. sigList];
        }

        // Parse sections: [{MusicSection: {...}}, ...] → Section[]
        if (element.TryGetProperty("sections", out JsonElement sections))
        {
            List<Section> secList = [];
            foreach (JsonElement sec in sections.EnumerateArray())
            {
                JsonElement inner = sec.TryGetProperty("MusicSection", out JsonElement msec) ? msec : sec;
                Section section = new();
                if (inner.TryGetProperty("sectionType", out JsonElement st))
                    section.SectionType = st.GetInt32();
                if (inner.TryGetProperty("marker", out JsonElement marker))
                    section.Marker = marker.GetSingle();
                if (inner.TryGetProperty("comment", out JsonElement comment))
                    section.Comment = comment.GetString() ?? "";
                secList.Add(section);
            }

            structure.Sections = [.. secList];
        }

        return structure;
    }

    public override void Write(Utf8JsonWriter writer, Structure value, JsonSerializerOptions options)
    {
        // Write without the wrapper
        JsonSerializer.Serialize(writer, value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}