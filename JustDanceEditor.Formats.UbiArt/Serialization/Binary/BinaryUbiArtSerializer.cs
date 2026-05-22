using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Binary;

public sealed class BinaryUbiArtSerializer(UbiArtEngineVersion? engineVersion) : IUbiArtSerializer
{
    public BinaryUbiArtSerializer()
        : this(null)
    {
    }

    internal LegacyBinarySerializerContext Context { get; } = new LegacyBinarySerializerContext(engineVersion.HasValue ? (int)engineVersion.Value : null);

    public T Deserialize<T>(Stream stream, JsonSerializerOptions? options = null) where T : new()
    {
        ArgumentNullException.ThrowIfNull(stream);
        return (T)LegacyBinarySerializer.Deserialize(typeof(T), stream, Context);
    }
}