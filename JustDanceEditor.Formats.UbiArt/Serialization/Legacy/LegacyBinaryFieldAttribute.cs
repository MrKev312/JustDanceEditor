namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal sealed class LegacyBinaryFieldAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
