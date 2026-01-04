using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services.Serialization;

public interface IUbiArtSerializer
{
    // Deserialize from raw bytes; for text-based serializers (JSON/LUA) implementations should decode UTF8 and trim trailing NULs
    T Deserialize<T>(byte[] content, JsonSerializerOptions? options = null) where T : new();
}