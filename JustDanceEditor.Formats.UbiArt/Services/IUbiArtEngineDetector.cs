namespace JustDanceEditor.Formats.UbiArt.Services;

public interface IUbiArtEngineDetector
{
    UbiArtVersionProfile Detect(string inputPath);
}