using NLua;

using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Serialization;

public static class LuaTableSerializer
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

        if (typeof(T) == typeof(Tapes.ClipTape))
        {
            return (T)(object)MapToClipTape(JsonSerializer.Deserialize<JsonElement>(json));
        }

        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
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
                            InfoComponent info = JsonSerializer.Deserialize<InfoComponent>(sanitizedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

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
                                            var rgba = new[] { a, r, g, b };

                                            if (keyStr.Equals("lyrics", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.lyrics = rgba;
                                            else if (keyStr.Equals("theme", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.theme = Array.ConvertAll(rgba, v => (int)(v * 255));
                                            else if (keyStr.Equals("songcolor_1a", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.songcolor_1a = rgba;
                                            else if (keyStr.Equals("songcolor_1b", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.songcolor_1b = rgba;
                                            else if (keyStr.Equals("songcolor_2a", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.songcolor_2a = rgba;
                                            else if (keyStr.Equals("songcolor_2b", StringComparison.OrdinalIgnoreCase))
                                                info.DefaultColors.songcolor_2b = rgba;
                                        }
                                    }
                                }
                            }

                            return new SongDesc { COMPONENTS = [info] };
                        }
                    }
                }
            }
        }

        throw new InvalidDataException("Could not find JD_SongDescTemplate in SongDesc LUA.");
    }

    private static Tapes.ClipTape MapToClipTape(JsonElement root)
    {
        if (root.TryGetProperty("Tape", out JsonElement tape))
        {
            return JsonSerializer.Deserialize<Tapes.ClipTape>(tape.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }

        throw new InvalidDataException("Could not find Tape in ClipTape LUA.");
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
            foreach (var item in list)
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
        foreach (var key in table.Keys)
        {
            object? value = table[key];
            string keyStr = key.ToString()!;

            if (value is LuaTable nestedTable)
            {
                if (IsArray(nestedTable))
                    dict[keyStr] = LuaTableToList(nestedTable);
                else
                    dict[keyStr] = LuaTableToDictionary(nestedTable);
            }
            else
            {
                dict[keyStr] = value!;
            }
        }

        return dict;
    }

    private static IList LuaTableToList(LuaTable table)
    {
        ArrayList list = [];
        foreach (var value in table.Values)
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
        foreach (var _ in table.Keys)
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
}