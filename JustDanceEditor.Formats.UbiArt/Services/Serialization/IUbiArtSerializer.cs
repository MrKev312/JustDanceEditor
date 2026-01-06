using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services.Serialization;

public interface IUbiArtSerializer
{
    // Deserialize from a Stream; for text-based serializers (JSON/LUA) implementations should decode UTF8 and trim trailing NULs
    T Deserialize<T>(Stream stream, JsonSerializerOptions? options = null) where T : new();
}