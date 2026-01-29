using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Formats.UbiArt.Services.Export;

/// <summary>
/// Handles generating the specific file content (JSON structures, XML, Lua, or Binary) for a specific engine version.
/// </summary>
public interface IEngineContentGenerator
{
    // Content Generators (Text or Binary)
    byte[] GenerateSongDesc(IntermediateSongPackage package);
    byte[] GenerateMusicTrack(IntermediateSongPackage package);
    byte[] GenerateDanceTape(IntermediateSongPackage package);
    byte[] GenerateKaraokeTape(IntermediateSongPackage package);
    byte[] GenerateAutodanceTape(IntermediateSongPackage package);
    byte[] GenerateMainSequenceTape(IntermediateSongPackage package);
    byte[] GenerateTapeCaseTpl(string mapName, string tapeType); // e.g. tapeType="dance"
    byte[] GenerateSequenceTpl();
    byte[] GenerateSoundTape(string mapName);
    byte[] GenerateAmbTpl(string mapName);
    byte[] GenerateMainSequenceTpl(string mapName);
    byte[] GenerateSgs();

    // Actor Generators (JSON wrapper for .act files or Binary .act)
    byte[] GenerateGenericActor(string className, string luaPath);

    // Scene Generators (ISC XML or Binary)
    byte[] GenerateMainScene(IntermediateSongPackage package);
    byte[] GenerateAudioScene(IntermediateSongPackage package);
    byte[] GenerateTimelineScene(IntermediateSongPackage package);
    byte[] GenerateCinematicsScene(IntermediateSongPackage package);
    byte[] GenerateMenuArtScene(IntermediateSongPackage package);
    byte[] GenerateAutodanceScene(IntermediateSongPackage package);
    byte[] GenerateGraphScene(string mapName);
    byte[] GenerateVideoScene(string mapName);
    byte[] GenerateVideoMapPreviewScene(string mapName);
    byte[] GenerateVideoPlayerActor(string mapName, bool isPreview);
    byte[] GenerateMpd();
    byte[] GenerateAutodanceActor(string mapName);
    byte[] GenerateMenuArtActor(string textureName, string mapName);
}