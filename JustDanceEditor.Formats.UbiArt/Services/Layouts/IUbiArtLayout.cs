using JustDanceEditor.Formats.UbiArt.Services;

namespace JustDanceEditor.Formats.UbiArt.Services.Layouts;

public interface IUbiArtLayout
{
    // Return the relative Map/World folder for a song given the container style and engine version
    string GetMapWorldFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion);

    string GetMediaFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion);
    string GetAudioFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion);
    string GetTimelineFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion);
    string GetPictosFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion);
    string GetMovesFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion);

    // Helper to locate the songdesc (relative path)
    string GetSongDescRelativePath(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion);
}