using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Formats.Unity.Core;

public class UnityConversionContext(ConversionRequest? request, FileSystem fileSystem)
{
    public ConversionRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));
    public IntermediateSongPackage IntermediatePackage { get; set; } = new();
    public FileSystem FileSystem { get; } = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    public Guid SongID => Request.SongGUID;
    
    public UnityExportData? UnityData { get; set; }

    public UnityExportData RequireUnityData() =>
        UnityData ?? throw new InvalidOperationException("Unity export data has not been initialized for this conversion context.");

    public string ResolveSongName()
    {
        if (!string.IsNullOrWhiteSpace(UnityData?.Name))
            return UnityData.Name;

        if (!string.IsNullOrWhiteSpace(Request.SongName))
            return Request.SongName!;
        return "Song";
    }

    public int ResolveCoachCount()
    {
        if (UnityData != null)
            return Math.Max(1, UnityData.Metadata.CoachCount);
        return 1;
    }

    public string? TryGetMovesFolder()
    {
        return FileSystem.GetFolderPath(FileSystem.InputFolders.MovesFolder, out string? folder)
            ? folder
            : null;
    }
}
