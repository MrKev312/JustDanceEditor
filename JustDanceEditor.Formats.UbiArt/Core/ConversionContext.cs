using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Formats.UbiArt.Core;

public class ConversionContext(ConversionRequest? request, FileSystem fileSystem)
{
    public ConversionRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));
    public IntermediateSongPackage IntermediatePackage { get; set; } = new();
    public FileSystem FileSystem { get; } = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    public Guid SongID => Request.SongGUID;
    public JDUbiArtSong SongData { get; set; } = new();

    public string ResolveSongName()
    {
        if (!string.IsNullOrWhiteSpace(SongData?.Name))
            return SongData.Name;

        if (!string.IsNullOrWhiteSpace(Request.SongName))
            return Request.SongName!;
        return "Song";
    }

    public int ResolveCoachCount()
    {
        if (SongData?.SongDesc?.COMPONENTS?.Length > 0)
            return Math.Max(1, SongData.SongDesc.COMPONENTS[0].NumCoach);
        return 1;
    }

    public string? TryGetMovesFolder()
    {
        return FileSystem.GetFolderPath(FileSystem.InputFolders.MovesFolder, out string? folder)
            ? folder
            : null;
    }
}
