using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Formats.UbiArt.Services.Export;

/// <summary>
/// Handles generating the specific file content (JSON structures, XML, Lua) for a specific engine version.
/// </summary>
public interface IEngineContentGenerator
{
    // Text (JSON/Lua) Generators
    string GenerateSongDesc(IntermediateSongPackage package);
    string GenerateMusicTrack(IntermediateSongPackage package);
    string GenerateDanceTape(IntermediateSongPackage package);
    string GenerateKaraokeTape(IntermediateSongPackage package);
    string GenerateAutodanceTape(IntermediateSongPackage package);
    string GenerateMainSequenceTape(IntermediateSongPackage package);
    string GenerateTapeCaseTpl(string mapName, string tapeType); // e.g. tapeType="dance"
    string GenerateSequenceTpl();
    string GenerateSoundTape(string mapName);
    string GenerateAmbTpl(string mapName);
    string GenerateMainSequenceTpl(string mapName);
    string GenerateSgs();

    // Actor Generators (JSON wrapper for .act files)
    string GenerateGenericActor(string className, string luaPath);

    // Scene Generators (ISC XML)
    string GenerateMainScene(IntermediateSongPackage package);
    string GenerateAudioScene(IntermediateSongPackage package);
    string GenerateTimelineScene(IntermediateSongPackage package);
    string GenerateCinematicsScene(IntermediateSongPackage package);
    string GenerateMenuArtScene(IntermediateSongPackage package);
    string GenerateAutodanceScene(IntermediateSongPackage package);
    string GenerateGraphScene();
    string GenerateVideoScene(string mapName);
    string GenerateVideoMapPreviewScene(string mapName);

    // Binary Generators
    byte[] GenerateVideoPlayerActor(string mapName, bool isPreview);
    byte[] GenerateMpd();
    byte[] GenerateAutodanceActor(string mapName);
    byte[] GenerateMenuArtActor(string textureName, string mapName);
}