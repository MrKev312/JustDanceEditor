namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class LegacyBinaryTypeIdAttribute(uint typeId) : Attribute
{
    public uint Value { get; } = typeId;

    public int MinEngineVersion { get; set; } = int.MinValue;

    public int MaxEngineVersion { get; set; } = int.MaxValue;

    public bool Matches(LegacyBinarySerializerContext context)
    {
        if (context.EngineVersion is not { } engineVersion)
            return true;

        return engineVersion >= MinEngineVersion && engineVersion <= MaxEngineVersion;
    }
}