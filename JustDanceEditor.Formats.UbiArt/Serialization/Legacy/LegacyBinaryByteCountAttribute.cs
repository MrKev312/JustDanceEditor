namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal sealed class LegacyBinaryByteCountAttribute(string memberName) : Attribute
{
    public string MemberName { get; } = memberName;

    public int Add { get; set; }
}