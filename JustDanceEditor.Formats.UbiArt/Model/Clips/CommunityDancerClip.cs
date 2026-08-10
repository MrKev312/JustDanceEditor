namespace JustDanceEditor.Formats.UbiArt.Model.Clips;

public sealed record CommunityDancerClip : Clip
{
    public override string Class { get; } = "CommunityDancerClip";
    public string DancerCountryCode { get; set; } = string.Empty;
    public int DancerAvatarId { get; set; }
    public string DancerName { get; set; } = string.Empty;
}
