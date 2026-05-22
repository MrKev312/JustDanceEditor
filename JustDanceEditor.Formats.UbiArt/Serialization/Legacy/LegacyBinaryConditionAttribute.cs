namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
internal sealed class LegacyBinaryConditionAttribute(string memberName, int value) : Attribute
{
    public string MemberName { get; } = memberName;

    public int Value { get; } = value;

    public bool Invert { get; set; }
}