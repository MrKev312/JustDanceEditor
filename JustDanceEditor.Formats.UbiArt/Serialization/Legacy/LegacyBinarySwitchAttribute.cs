namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal sealed class LegacyBinarySwitchAttribute(string memberName) : Attribute
{
    public string MemberName { get; } = memberName;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class LegacyBinarySwitchCaseAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}