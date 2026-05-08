using JustDanceEditor.Formats.UbiArt.Import;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

public class JD2017EngineContentGenerator(UbiArtEngineVersion EngineVersion) : ModernEngineContentGenerator(EngineVersion)
{
    public override object GenerateMenuArtActor(string textureName, string mapName)
    {
        return new UbiArtMenuArtActorFile(textureName, mapName, UbiArtMenuArtActorVersion.Jd2017);
    }
}
