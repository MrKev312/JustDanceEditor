namespace JustDanceEditor.Formats.UbiArt.Import;

public interface IUbiArtEngineDetector
{
    UbiArtVersionProfile Detect(string inputPath);
}