using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;

using NLua;

using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.UbiArt.Serialization;

public static partial class LuaTableSerializer
{
    private static void InitializeLua(Lua lua)
    {
        lua.DoString("function includeReference(path) end");
        lua.DoString("NumCoach = { Solo = 1, Duo = 2, Trio = 3, Quatuor = 4 }");
        lua.DoString("SongDifficulty = { Easy = 1, Normal = 2, Hard = 3, Extreme = 4 }");
        lua.DoString("GameMode = { Classic = 0 }");
        lua.DoString("GameModeFlags = { None = 0 }");
        lua.DoString("GameModeStatus = { Available = 0 }");
        lua.DoString("structure = { }"); // For MusicTrack
    }

    private static string ResolveLuaIncludes(string luaContent, LayeredFileSystem fileSystem)
    {
        // Find all includeReference() calls and load the referenced files
        StringBuilder result = new(luaContent);

        // Match includeReference("path") - single argument pattern
        Regex includeRegex = IncludeReferenceRegex();

        foreach (Match match in includeRegex.Matches(luaContent))
        {
            string filePath = match.Groups[1].Value;

            try
            {
                if (fileSystem.GetFilePath(filePath, out CookedFile? cookedFile))
                {
                    using Stream stream = fileSystem.GetFileStream(cookedFile);
                    using StreamReader reader = new(stream, Encoding.UTF8);
                    string includedContent = reader.ReadToEnd().TrimEnd('\0');

                    // Replace the includeReference call with the actual file content
                    result.Replace(match.Value, includedContent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load included reference '{filePath}': {ex.Message}");
            }
        }

        return result.ToString();
    }

    public static T Deserialize<T>(string luaContent) where T : new()
    {
        using Lua lua = new();
        InitializeLua(lua);

        lua.DoString(luaContent);

        LuaTable? paramsTable = lua["params"] as LuaTable ?? throw new InvalidDataException("LUA script did not define 'params' table.");
        IDictionary<string, object> dict = LuaTableToDictionary(paramsTable);
        string json = JsonSerializer.Serialize(dict);
        Console.WriteLine($"DEBUG JSON: {json}");

        if (typeof(T) == typeof(SongDesc))
        {
            return (T)(object)MapToSongDesc(JsonSerializer.Deserialize<JsonElement>(json));
        }

        if (typeof(T) == typeof(ClipTape))
        {
            return (T)(object)MapToClipTape(JsonSerializer.Deserialize<JsonElement>(json));
        }

        if (typeof(T) == typeof(MusicTrack))
        {
            return (T)(object)MapToMusicTrack(JsonSerializer.Deserialize<JsonElement>(json));
        }

        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException($"Failed to deserialize Lua content to {typeof(T).Name}.");
    }

    public static T Deserialize<T>(string luaContent, LayeredFileSystem fileSystem) where T : new()
    {
        using Lua lua = new();
        InitializeLua(lua);

        // Pre-process to resolve includeReference calls
        string processedContent = ResolveLuaIncludes(luaContent, fileSystem);

        lua.DoString(processedContent);

        LuaTable? paramsTable = lua["params"] as LuaTable ?? throw new InvalidDataException("LUA script did not define 'params' table.");

        // If requesting MusicTrack, extract it directly from the Lua VM to capture globals like 'structure'
        if (typeof(T) == typeof(MusicTrack))
        {
            // Navigate to Actor_Template > COMPONENTS
            if (paramsTable["Actor_Template"] is LuaTable actorTemplate && actorTemplate["COMPONENTS"] is LuaTable components)
            {
                foreach (object? compObj in components.Values)
                {
                    if (compObj is LuaTable compTable && compTable["MusicTrackComponent_Template"] is LuaTable mtComp)
                    {
                        object? tdObj = mtComp["trackData"];
                        if (tdObj is LuaTable trackDataTable)
                        {
                            // MusicTrackData may be nested under MusicTrackData or be the trackData itself
                            object? mtdObj = trackDataTable["MusicTrackData"] ?? trackDataTable;
                            if (mtdObj is LuaTable mtdTable)
                            {
                                // Convert mtdTable to JSON via LuaTableToDictionary and then to TrackData
                                IDictionary<string, object> mtdDict = LuaTableToDictionary(mtdTable);

                                // If structure is a LuaTable at top-level, replace it with converted object
                                if (mtdTable["structure"] is LuaTable structureTable)
                                {
                                    mtdDict["structure"] = LuaTableToDictionary(structureTable);
                                }

                                string mtdJson = JsonSerializer.Serialize(mtdDict);
                                JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
                                options.Converters.Add(new FloatArrayFlexibleJsonConverter());
                                options.Converters.Add(new StructureJsonConverter());
                                options.Converters.Add(new Model.Clips.ClipConverter());

                                TrackData trackData = JsonSerializer.Deserialize<TrackData>(mtdJson, options)
                                    ?? throw new JsonException("Failed to deserialize TrackData from Lua content.");
                                TrackDataHolder holder = new() { Class = "MusicTrackComponent_Template", TrackData = trackData };
                                MusicTrack musicTrack = new() { Class = "MusicTrack", Components = [holder] };
                                return (T)(object)musicTrack;
                            }
                        }
                    }
                }
            }

            throw new InvalidDataException("Could not extract MusicTrack from Actor_Template structure via Lua.");
        }

        IDictionary<string, object> dict = LuaTableToDictionary(paramsTable);
        string json = JsonSerializer.Serialize(dict);
        Console.WriteLine($"DEBUG JSON: {json}");

        if (typeof(T) == typeof(SongDesc))
        {
            return (T)(object)MapToSongDesc(JsonSerializer.Deserialize<JsonElement>(json));
        }

        if (typeof(T) == typeof(ClipTape))
        {
            return (T)(object)MapToClipTape(JsonSerializer.Deserialize<JsonElement>(json));
        }

        if (typeof(T) == typeof(MusicTrack))
        {
            return (T)(object)MapToMusicTrack(JsonSerializer.Deserialize<JsonElement>(json));
        }

        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException($"Failed to deserialize Lua content to {typeof(T).Name}.");
    }

    private static SongDesc MapToSongDesc(JsonElement root)
    {
        JsonElement actorTemplate = default;
        foreach (JsonProperty prop in root.EnumerateObject())
        {
            if (prop.NameEquals("Actor_Template"))
            {
                actorTemplate = prop.Value;
                break;
            }
        }

        if (actorTemplate.ValueKind != JsonValueKind.Undefined)
        {
            if (actorTemplate.TryGetProperty("COMPONENTS", out JsonElement components))
            {
                foreach (JsonElement component in components.EnumerateArray())
                {
                    foreach (JsonProperty prop in component.EnumerateObject())
                    {
                        if (prop.NameEquals("JD_SongDescTemplate"))
                        {
                            // Create a copy of the component object without DefaultColors to avoid deserialization error
                            Dictionary<string, object> dict = [];
                            foreach (JsonProperty componentProp in prop.Value.EnumerateObject())
                            {
                                if (!componentProp.NameEquals("DefaultColors"))
                                {
                                    dict[componentProp.Name] = componentProp.Value;
                                }
                            }

                            string sanitizedJson = JsonSerializer.Serialize(dict);
                            InfoComponent info = JsonSerializer.Deserialize<InfoComponent>(sanitizedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                ?? throw new JsonException("Failed to deserialize InfoComponent from Lua content.");

                            // Manually map DefaultColors if it was an array
                            if (prop.Value.TryGetProperty("DefaultColors", out JsonElement colors) && colors.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement item in colors.EnumerateArray())
                                {
                                    if (item.TryGetProperty("KEY", out JsonElement key) && item.TryGetProperty("VAL", out JsonElement val))
                                    {
                                        string keyStr = key.GetString() ?? "";
                                        string colorStr = val.GetString() ?? "";
                                        if (colorStr.StartsWith("0x"))
                                            colorStr = colorStr[2..];
                                        if (colorStr.Length == 8)
                                        {
                                            float a = Convert.ToInt32(colorStr[..2], 16) / 255.0f;
                                            float r = Convert.ToInt32(colorStr.Substring(2, 2), 16) / 255.0f;
                                            float g = Convert.ToInt32(colorStr.Substring(4, 2), 16) / 255.0f;
                                            float b = Convert.ToInt32(colorStr.Substring(6, 2), 16) / 255.0f;
                                            float[] rgba = [a, r, g, b];

                                            if (keyStr.Equals("lyrics", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.Lyrics = rgba;
                                            else if (keyStr.Equals("theme", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.Theme = Array.ConvertAll(rgba, v => (int)(v * 255));
                                            else if (keyStr.Equals("songcolor_1a", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.SongColor1a = rgba;
                                            else if (keyStr.Equals("songcolor_1b", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.SongColor1b = rgba;
                                            else if (keyStr.Equals("songcolor_2a", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.SongColor2a = rgba;
                                            else if (keyStr.Equals("songcolor_2b", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.SongColor2b = rgba;
                                        }
                                    }
                                }
                            }

                            return new SongDesc { Components = [info] };
                        }
                    }
                }
            }
        }

        throw new InvalidDataException("Could not find JD_SongDescTemplate in SongDesc LUA.");
    }

    private static ClipTape MapToClipTape(JsonElement root)
    {
        if (root.TryGetProperty("Tape", out JsonElement tape))
        {
            // Normalize clip entries to include a __class property and flatten wrapper objects.
            // We need to deep-copy the JSON to avoid "node already has a parent" errors.
            List<JsonNode> normalizedClips = [];

            if (tape.TryGetProperty("Clips", out JsonElement clipsElement) && clipsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement clipElement in clipsElement.EnumerateArray())
                {
                    if (JsonNode.Parse(clipElement.GetRawText()) is not JsonObject clipObj)
                        continue;

                    string? className = null;

                    // Try to determine class name from NAME or wrapper property
                    if (clipObj.TryGetPropertyValue("NAME", out JsonNode? nameNode) && nameNode is JsonValue nameVal)
                        className = nameVal.ToString();

                    if (string.IsNullOrEmpty(className))
                    {
                        foreach (KeyValuePair<string, JsonNode?> kv in clipObj)
                        {
                            if (kv.Value is JsonObject && kv.Key.EndsWith("Clip", StringComparison.Ordinal))
                            {
                                className = kv.Key;
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(className))
                        continue;

                    // If there is a wrapper object like { "MotionClip": { ... } }, extract and flatten it
                    JsonObject normalized = [];

                    if (clipObj.TryGetPropertyValue(className, out JsonNode? inner) && inner is JsonObject innerObj)
                    {
                        // Deep-copy all properties from inner object
                        foreach (KeyValuePair<string, JsonNode?> innerKv in innerObj)
                        {
                            if (innerKv.Value != null)
                                normalized[innerKv.Key] = JsonNode.Parse(innerKv.Value.ToJsonString());
                        }
                    }

                    // Add __class marker for the ClipConverter
                    if (!normalized.ContainsKey("__class"))
                        normalized["__class"] = className;

                    normalizedClips.Add(normalized);
                }
            }

            // Rebuild the Tape object with normalized Clips
            JsonObject resultTape = [];

            JsonArray normalizedClipsArray = [.. normalizedClips];
            resultTape["Clips"] = normalizedClipsArray;

            // Copy other Tape properties (Tracks, MapName, TapeClock, etc.)
            foreach (JsonProperty prop in tape.EnumerateObject())
            {
                if (!prop.NameEquals("Clips"))
                {
                    resultTape[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                }
            }

            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new Model.Clips.ClipConverter());
            options.Converters.Add(new FloatArrayFlexibleJsonConverter());

            return JsonSerializer.Deserialize<ClipTape>(resultTape.ToJsonString(), options)
                ?? throw new JsonException("Failed to deserialize ClipTape from Lua content.");
        }

        throw new InvalidDataException("Could not find Tape in ClipTape LUA.");
    }

    private static MusicTrack MapToMusicTrack(JsonElement root)
    {
        // MusicTrack from Lua is wrapped as Actor_Template > COMPONENTS
        // We need to extract MusicTrackComponent_Template > trackData > MusicTrackData

        if (root.TryGetProperty("Actor_Template", out JsonElement actorTemplate))
        {
            if (actorTemplate.TryGetProperty("COMPONENTS", out JsonElement components) && components.ValueKind == JsonValueKind.Array)
            {
                JsonElement.ArrayEnumerator componentArray = components.EnumerateArray();
                foreach (JsonElement component in componentArray)
                {
                    // Look for MusicTrackComponent_Template
                    foreach (JsonProperty prop in component.EnumerateObject())
                    {
                        if (prop.NameEquals("MusicTrackComponent_Template"))
                        {
                            if (prop.Value.TryGetProperty("trackData", out JsonElement trackData))
                            {
                                // Extract the MusicTrackData from the wrapper
                                JsonElement musicTrackData;
                                if (trackData.TryGetProperty("MusicTrackData", out JsonElement mtd))
                                {
                                    musicTrackData = mtd;
                                }
                                else
                                {
                                    musicTrackData = trackData;
                                }

                                // Create options with necessary converters
                                JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
                                options.Converters.Add(new StructureJsonConverter());
                                options.Converters.Add(new FloatArrayFlexibleJsonConverter());

                                // Create a wrapper MusicTrack with a single component
                                TrackDataHolder trackDataHolder = new()
                                {
                                    Class = "MusicTrackComponent_Template",
                                    TrackData = JsonSerializer.Deserialize<TrackData>(
                                        musicTrackData.GetRawText(),
                                        options
                                    ) ?? throw new JsonException("Failed to deserialize TrackData from music track Lua content.")
                                };

                                MusicTrack musicTrack = new()
                                {
                                    Class = "MusicTrack",
                                    Components = [trackDataHolder]
                                };

                                return musicTrack;
                            }
                        }
                    }
                }
            }
        }

        throw new InvalidDataException("Could not extract MusicTrack from Actor_Template structure.");
    }

    public static string Serialize<T>(T obj)
    {
        // For now, let's implement a basic LUA table generator
        // This is complex for general objects, but we can handle our specific types
        StringBuilder sb = new();
        sb.AppendLine("params =");
        SerializeObject(sb, obj, 0);
        return sb.ToString();
    }

    private static void SerializeObject(StringBuilder sb, object? obj, int indent)
    {
        if (obj == null)
        {
            sb.Append("nil");
            return;
        }

        string indentation = new(' ', indent * 2);

        if (obj is IDictionary dict)
        {
            sb.AppendLine("{");
            foreach (DictionaryEntry entry in dict)
            {
                sb.Append(indentation + "  ");
                sb.Append(entry.Key + " = ");
                SerializeObject(sb, entry.Value, indent + 1);
                sb.AppendLine(",");
            }

            sb.Append(indentation + "}");
        }
        else if (obj is IEnumerable list and not string)
        {
            sb.AppendLine("{");
            foreach (object? item in list)
            {
                sb.Append(indentation + "  ");
                SerializeObject(sb, item, indent + 1);
                sb.AppendLine(",");
            }

            sb.Append(indentation + "}");
        }
        else if (obj is string s)
        {
            sb.Append($"\"{s}\"");
        }
        else if (obj is bool b)
        {
            sb.Append(b ? "true" : "false");
        }
        else if (obj.GetType().IsPrimitive || obj is decimal || obj is float || obj is double)
        {
            // Use CultureInfo.InvariantCulture to ensure dot as decimal separator
            if (obj is IConvertible conv)
                sb.Append(conv.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else
                sb.Append(obj.ToString());
        }
        else if (obj.GetType().IsClass || obj.GetType().IsValueType)
        {
            // Handle anonymous types or classes
            sb.AppendLine("{");
            foreach (PropertyInfo prop in obj.GetType().GetProperties())
            {
                if (prop.GetIndexParameters().Length > 0)
                    continue; // Skip indexed properties
                sb.Append(indentation + "  ");
                sb.Append(prop.Name + " = ");
                SerializeObject(sb, prop.GetValue(obj), indent + 1);
                sb.AppendLine(",");
            }

            sb.Append(indentation + "}");
        }
        else
        {
            sb.Append(obj.ToString());
        }
    }

    private static IDictionary<string, object> LuaTableToDictionary(LuaTable table)
    {
        Dictionary<string, object> dict = [];
        foreach (object? key in table.Keys)
        {
            object? value = table[key];
            string keyStr = key?.ToString() ?? throw new InvalidDataException("Lua table contained a null key.");

            if (value is LuaTable nestedTable)
            {
                if (IsArray(nestedTable))
                    dict[keyStr] = LuaTableToList(nestedTable);
                else
                    dict[keyStr] = LuaTableToDictionary(nestedTable);
            }
            else
            {
                dict[keyStr] = value ?? throw new InvalidDataException($"Lua table value for key '{keyStr}' was null.");
            }
        }

        return dict;
    }

    private static IList LuaTableToList(LuaTable table)
    {
        ArrayList list = [];
        foreach (object? value in table.Values)
        {
            if (value is LuaTable nestedTable)
            {
                if (IsArray(nestedTable))
                    list.Add(LuaTableToList(nestedTable));
                else
                    list.Add(LuaTableToDictionary(nestedTable));
            }
            else
            {
                list.Add(value);
            }
        }

        return list;
    }

    private static bool IsArray(LuaTable table)
    {
        int count = 0;
        foreach (object? _ in table.Keys)
            count++;
        if (count == 0)
            return true; // Default empty tables to arrays for better JSON compatibility

        for (int i = 1; i <= count; i++)
        {
            if (table[i] == null)
                return false;
        }

        return true;
    }

    [GeneratedRegex(@"includeReference\s*\(\s*""([^""]+)""\s*\)", RegexOptions.IgnoreCase
, "en-US")]
    private static partial Regex IncludeReferenceRegex();
}