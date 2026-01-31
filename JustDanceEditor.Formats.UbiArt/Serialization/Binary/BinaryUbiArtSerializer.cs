using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Binary;

// Placeholder for binary formats (JD2014/JD2015). Not implemented yet.
public class BinaryUbiArtSerializer : IUbiArtSerializer
{
    public T Deserialize<T>(Stream stream, JsonSerializerOptions? options = null) where T : new()
    {
        // For now, binary parsing is not implemented. The method will throw if used.
        throw new NotImplementedException("Binary serializer is not implemented yet.");
    }
}