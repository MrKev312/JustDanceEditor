namespace JustDanceEditor.Formats.JDI;

public enum ExportType
{
    OfflineCache,
    CustomServer
}

// TODO: Entirely rethink this class, it has Unity and UbiArt specific stuff in it
// which breaks the separation of concerns. Maybe tie in the UI layer more closely
// by having both formats have their own specific request types that are filled in
// by the UI layer.
public class ConversionRequest
{
    // Folder where the input files are located
    public required string InputPath { get; set; }
    // Folder where the output will be saved
    public required string OutputPath { get; set; }
    // Folder where the template is located
    public required string TemplatePath { get; set; }
    // Type of export
    public ExportType ExportType { get; set; } = ExportType.CustomServer;
    // Should the cover be looked up online
    public bool OnlineCover { get; set; } = true;
    // Name of the song (optional)
    public string? SongName { get; set; } = null;
    // GUID of the song
    public Guid SongGUID { get; set; } = Guid.NewGuid();
    // Cache number
    public uint? CacheNumber { get; set; } = null;
    public uint? JDVersion { get; set; } = null;
}