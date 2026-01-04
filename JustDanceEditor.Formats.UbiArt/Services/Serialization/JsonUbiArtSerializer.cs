using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services.Serialization;

public class JsonUbiArtSerializer : IUbiArtSerializer
{
    public T Deserialize<T>(byte[] content, JsonSerializerOptions? options = null) where T : new()
    {
        options ??= new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        string text = Encoding.UTF8.GetString(content).TrimEnd('\0');
        return JsonSerializer.Deserialize<T>(text, options)!;
    }
}