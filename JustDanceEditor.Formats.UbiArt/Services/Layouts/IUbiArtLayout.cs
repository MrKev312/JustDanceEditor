namespace JustDanceEditor.Formats.UbiArt.Services.Layouts;

public interface IUbiArtLayout
{
    // Return the relative Map/World folder for a song given the platform and engine version
    string GetMapWorldFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion);

    string GetMediaFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion);
    string GetAudioFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion);
    string GetTimelineFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion);
    string GetPictosFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion);
    string GetMovesFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion);

    // Helper to locate the songdesc (relative path)
    string GetSongDescRelativePath(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion);
}