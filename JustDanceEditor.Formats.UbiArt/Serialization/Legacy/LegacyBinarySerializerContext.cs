namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal readonly record struct LegacyBinarySerializerContext(int? EngineVersion)
{
    public static LegacyBinarySerializerContext None { get; } = new(null);
}