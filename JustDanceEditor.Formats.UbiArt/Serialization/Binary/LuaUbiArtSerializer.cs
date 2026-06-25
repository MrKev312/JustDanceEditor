using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Binary;

public class LuaUbiArtSerializer : IUbiArtSerializer
{
    public T Deserialize<T>(Stream stream, JsonSerializerOptions? options = null) where T : new()
    {
        // LuaTableSerializer expects a string; decode stream and trim NULs
        using StreamReader sr = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
        string text = sr.ReadToEnd().TrimEnd('\0');
        return LuaTableSerializer.Deserialize<T>(text);
    }
}