using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Formats.UbiArt.Export;

/// <summary>
/// Handles generating the specific file content (JSON structures, XML, Lua, or Binary) for a specific engine version.
/// </summary>
public interface IEngineContentGenerator
{
    // Content Generators (Text or Binary)
    object GenerateSongDesc(IntermediateSongPackage package);
    object GenerateMusicTrack(IntermediateSongPackage package);
    object GenerateDanceTape(IntermediateSongPackage package);
    object GenerateKaraokeTape(IntermediateSongPackage package);
    object GenerateAutodanceTape(IntermediateSongPackage package);
    object GenerateMainSequenceTape(IntermediateSongPackage package);
    object GenerateTapeCaseTpl(string mapName, string tapeType); // e.g. tapeType="dance"
    object GenerateSequenceTpl();
    object GenerateSoundTape(string mapName);
    object GenerateAmbTpl(string mapName);
    object GenerateMainSequenceTpl(string mapName);
    object GenerateSgs();

    // Actor Generators (JSON wrapper for .act files or Binary .act)
    object GenerateGenericActor(string className, string luaPath);

    // Scene Generators (ISC XML or Binary)
    object GenerateMainScene(IntermediateSongPackage package);
    object GenerateAudioScene(IntermediateSongPackage package);
    object GenerateTimelineScene(IntermediateSongPackage package);
    object GenerateCinematicsScene(IntermediateSongPackage package);
    object GenerateMenuArtScene(IntermediateSongPackage package);
    object GenerateAutodanceScene(IntermediateSongPackage package);
    object GenerateGraphScene(string mapName);
    object GenerateVideoScene(string mapName);
    object GenerateVideoMapPreviewScene(string mapName);
    object GenerateVideoPlayerActor(string mapName, bool isPreview);
    object GenerateMpd();
    object GenerateAutodanceActor(string mapName);
    object GenerateMenuArtActor(string textureName, string mapName);
}
