using JustDanceEditor.Formats.UbiArt.Serialization;

using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services.Serialization;

public class LuaUbiArtSerializer : IUbiArtSerializer
{
    public T Deserialize<T>(byte[] content, JsonSerializerOptions? options = null) where T : new()
    {
        // LuaTableSerializer expects a string; decode bytes and trim NULs
        string text = Encoding.UTF8.GetString(content).TrimEnd('\0');
        return LuaTableSerializer.Deserialize<T>(text);
    }
}