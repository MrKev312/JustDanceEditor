using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Binary;

public class JsonUbiArtSerializer : IUbiArtSerializer
{
    public T Deserialize<T>(Stream stream, JsonSerializerOptions? options = null) where T : new()
    {
        options ??= new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        using StreamReader sr = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
        string text = sr.ReadToEnd().TrimEnd('\0');
        return JsonSerializer.Deserialize<T>(text, options) ?? throw new JsonException($"Failed to deserialize JSON payload to {typeof(T).Name}.");
    }
}