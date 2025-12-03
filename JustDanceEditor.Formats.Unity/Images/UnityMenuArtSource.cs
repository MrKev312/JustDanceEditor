namespace JustDanceEditor.Formats.Unity.Images;

public sealed record UnityMenuArtSource(
    string? CoverPath,
    string? SongTitleLogoPath,
    string? CoachesBackgroundPath,
    IReadOnlyList<string> CoachImagePaths);
