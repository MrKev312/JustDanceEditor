using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Converter.Core;

public partial class ConversionContext(ConversionRequest? request, FileSystem fileSystem)
{
    public ConversionRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));
    public IntermediateSongPackage IntermediatePackage { get; set; } = new();
    public FileSystem FileSystem { get; } = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    public Guid SongID => Request.SongGUID;

    public string ResolveSongName()
    {
        string? name = TryGetSongNameFromUbiArt();
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        name = TryGetSongNameFromUnity();
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        if (!string.IsNullOrWhiteSpace(Request.SongName))
            return Request.SongName!;
        return "Song";
    }

    public int ResolveCoachCount()
    {
        int? fromUbiArt = TryGetCoachCountFromUbiArt();
        if (fromUbiArt.HasValue)
            return Math.Max(1, fromUbiArt.Value);

        int? fromUnity = TryGetCoachCountFromUnity();
        if (fromUnity.HasValue)
            return Math.Max(1, fromUnity.Value);

        return 1;
    }

    public string? TryGetMovesFolder()
    {
        return FileSystem.GetFolderPath(FileSystem.InputFolders.MovesFolder, out string? folder)
            ? folder
            : null;
    }

    private partial string? TryGetSongNameFromUbiArt();
    private partial string? TryGetSongNameFromUnity();
    private partial int? TryGetCoachCountFromUbiArt();
    private partial int? TryGetCoachCountFromUnity();
}