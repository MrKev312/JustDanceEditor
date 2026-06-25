namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
internal sealed class LegacyBinaryEngineVersionConditionAttribute : Attribute
{
    public int MinEngineVersion { get; set; } = int.MinValue;

    public int MaxEngineVersion { get; set; } = int.MaxValue;

    public bool Matches(LegacyBinarySerializerContext context)
    {
        if (context.EngineVersion is not { } engineVersion)
            return true;

        return engineVersion >= MinEngineVersion && engineVersion <= MaxEngineVersion;
    }
}