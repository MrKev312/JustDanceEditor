namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal sealed class LegacyBinaryPaddingAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}