using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.FileSystem;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.UbiArt.Import.Assets;

public class FileSystemAssetResolver(IUbiArtLayout layout, JustDanceUbiArtFileSystem fileSystem, JDI.Services.IFileSystem? io = null) : IUbiArtAssetResolver
{
    private readonly IUbiArtLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly JustDanceUbiArtFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly JDI.Services.IFileSystem _io = io ?? new JDI.Services.SystemFileSystem();

    private readonly string[] AudioExtensions = [".ogg", ".opus", ".wav", ".wem"];
    private string AssetSongName => string.IsNullOrWhiteSpace(_fileSystem.ContentSongName)
        ? _fileSystem.SongName
        : _fileSystem.ContentSongName;

    public CookedFile[] GetPictograms()
    {
        try
        {
            CookedFile[] files = _fileSystem.GetAllFiles(_fileSystem.InputFolders.PictosFolder, "*");
            return files;
        }
        catch
        {
            return [];
        }
    }

    public CookedFile? GetCoverArt()
    {
        string pattern = $"{AssetSongName}_cover_*";
        CookedFile[] files = GetMenuArtFiles(pattern);
        if (files.Length > 0)
            return files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).First();

        // Try more generic patterns
        files = GetMenuArtFiles("*cover*");
        return files.FirstOrDefault();
    }

    public CookedFile[] GetCoachTextures()
    {
        return
        [
            .. EnumerateMenuArtFiles($"{AssetSongName}_coach_*", $"{AssetSongName}_coach*")
                .Where(IsIndividualCoachTexture)
                .GroupBy(GetLogicalMenuArtName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(GetCoachOrdinal)
                .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public CookedFile? GetAlbumCoach()
    {
        return EnumerateMenuArtFiles(
                $"{AssetSongName}_cover_albumcoach.*",
                $"{AssetSongName}_albumcoach.*",
                $"{AssetSongName}_AlbumCoach.*",
                "*albumcoach*",
                "*AlbumCoach*")
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public CookedFile? GetBackgroundTexture()
    {
        CookedFile[] candidates = GetMenuArtFiles($"{AssetSongName}_map_bkg.*");
        if (candidates.Length > 0)
            return candidates[0];

        candidates = GetMenuArtFiles($"{AssetSongName}_banner_bkg.*");
        return candidates.FirstOrDefault();
    }

    public CookedFile[] GetMoveFiles()
    {
        List<CookedFile> files = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string movesFolder in EnumerateMoveFolders())
        {
            try
            {
                foreach (CookedFile file in _fileSystem.GetAllFiles(movesFolder, "*.msm"))
                {
                    if (seen.Add(file.RelativePath))
                        files.Add(file);
                }
            }
            catch
            {
            }
        }

        return [.. files];
    }

    private IEnumerable<string> EnumerateMoveFolders()
    {
        string timelineMovesFolder = Path.Combine(_fileSystem.InputFolders.TimelineFolder, "moves");

        yield return _fileSystem.InputFolders.MovesFolder;
        yield return timelineMovesFolder;

        foreach (UbiArtPlatform platform in Enum.GetValues<UbiArtPlatform>())
        {
            if (platform == UbiArtPlatform.Uncooked)
                continue;

            string platformFolder = platform.GetCookedFolderName();
            if (!string.IsNullOrWhiteSpace(platformFolder))
                yield return Path.Combine(timelineMovesFolder, platformFolder);
        }
    }

    public string? FindFirstVideoFile()
    {
        return UbiArtVideoFileSelector.FindPreferredVideoFile(_fileSystem)?.RelativePath;
    }

    public bool TryFindAudio(string relativePath, [NotNullWhen(true)] out CookedFile? file)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            file = null;
            return false;
        }

        // Use the file system's existing resolution first
        if (_fileSystem.GetFilePath(relativePath, out file) && IsSupportedAudioFile(file))
            return true;

        file = null;

        // Fallback via extensions
        string baseName = Path.GetFileNameWithoutExtension(relativePath);
        string folder = Path.GetDirectoryName(relativePath) ?? string.Empty;
        return TryFindFileWithExtensions(folder, baseName, AudioExtensions, out file);
    }

    public bool TryFindMainAudio(JDUbiArtSong songData, [NotNullWhen(true)] out CookedFile? file, out bool isPreMerged)
    {
        isPreMerged = false;

        string[] preMergedAudioFolders = _fileSystem.GetFolderPath(_fileSystem.InputFolders.MediaFolder, out _)
            ? [_fileSystem.InputFolders.MediaFolder, _fileSystem.InputFolders.AudioFolder]
            : [_fileSystem.InputFolders.AudioFolder];

        foreach (string audioFolder in preMergedAudioFolders)
        {
            CookedFile[] oggFiles = _fileSystem.GetAllFiles(audioFolder, "*.ogg");
            if (oggFiles.Length > 0)
            {
                isPreMerged = true;
                file = oggFiles[0];
                return true;
            }
        }

        // Try the MusicTrack path
        string relativePath = songData.MusicTrack.Components[0].TrackData.Path;
        if (!string.IsNullOrWhiteSpace(relativePath) && TryFindAudio(relativePath, out file))
        {
            isPreMerged = IsPreMergedAudioFile(file);
            return true;
        }

        string songName = string.IsNullOrWhiteSpace(songData.Name)
            ? _fileSystem.SongName
            : songData.Name;
        if (!string.IsNullOrWhiteSpace(songName) &&
            TryFindFileWithExtensions(_fileSystem.InputFolders.AudioFolder, songName, AudioExtensions, out file))
        {
            return true;
        }

        file = null;
        return false;
    }

    public bool TryFindFileWithExtensions(string folderRelative, string baseName, IEnumerable<string> extensions, [NotNullWhen(true)] out CookedFile? file)
    {
        file = null;
        if (string.IsNullOrWhiteSpace(baseName))
            return false;

        // Build ordered list of extensions to try, including cooked variants if necessary
        List<string> extList = [.. extensions];
        if (_fileSystem.VersionProfile.Platform == UbiArtPlatform.Cafe)
        {
            // For each extension add extension + .ckd as higher priority
            List<string> cookedExts = [];
            foreach (string e in extList)
            {
                cookedExts.Add(e + ".ckd");
            }
            // Try cooked variants first
            extList = [.. cookedExts.Concat(extList)];
        }

        foreach (string ext in extList)
        {
            string candidateRelative = Path.Combine(folderRelative, baseName + ext);

            // Try absolute path quick check
            string candidateAbsolute = _io.Combine(_fileSystem.ConversionRequest.InputPath, candidateRelative);
            if (_io.FileExists(candidateAbsolute))
            {
                file = new CookedFile(candidateAbsolute);
                return true;
            }

            if (_fileSystem.GetFilePath(candidateRelative, out CookedFile? found))
            {
                file = found;
                return true;
            }
        }

        // Also try pattern-based matches in folder
        try
        {
            CookedFile[] all = _fileSystem.GetAllFiles(folderRelative);
            foreach (CookedFile f in all)
            {
                string fileName = Path.GetFileName(f.RelativePath);
                string rootName = fileName.Split('.')[0]; // strip all extensions
                if (string.Equals(rootName, baseName, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsSupportedAudioFile(f))
                    {
                        file = f;
                        return true;
                    }
                }

                // Also consider names like baseName_suffix
                if (fileName.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsSupportedAudioFile(f))
                    {
                        file = f;
                        return true;
                    }
                }
            }
        }
        catch { }

        // As a final fallback, check absolute path for plain baseName (no extension)
        string fallbackAbsolute = _io.Combine(_fileSystem.ConversionRequest.InputPath, folderRelative, baseName);
        if (!string.Equals(Path.GetFullPath(fallbackAbsolute), Path.GetFullPath(_fileSystem.ConversionRequest.InputPath), StringComparison.OrdinalIgnoreCase) &&
            _io.FileExists(fallbackAbsolute))
        {
            file = new CookedFile(fallbackAbsolute);
            return true;
        }

        return false;
    }

    private bool IsSupportedAudioFile(CookedFile file) =>
        AudioExtensions.Any(extension => file.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));

    private CookedFile[] GetMenuArtFiles(string pattern)
    {
        try
        {
            return _fileSystem.GetAllFiles(_fileSystem.InputFolders.MenuArtFolder, pattern);
        }
        catch
        {
            return [];
        }
    }

    private IEnumerable<CookedFile> EnumerateMenuArtFiles(params string[] patterns)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string pattern in patterns)
        {
            foreach (CookedFile file in GetMenuArtFiles(pattern).OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(file.RelativePath))
                    yield return file;
            }
        }
    }

    private static bool IsIndividualCoachTexture(CookedFile file)
    {
        string logicalName = GetLogicalMenuArtName(file);

        return IsTextureExtension(file.Extension) &&
            logicalName.Contains("_coach", StringComparison.OrdinalIgnoreCase) &&
            !logicalName.Contains("albumcoach", StringComparison.OrdinalIgnoreCase) &&
            !logicalName.Contains("phone", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextureExtension(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static string GetLogicalMenuArtName(CookedFile file)
    {
        string fileName = file.RelativePath.Replace('\\', '/').Split('/')[^1];
        int extensionIndex = fileName.IndexOf('.');
        return extensionIndex >= 0
            ? fileName[..extensionIndex]
            : fileName;
    }

    private static int GetCoachOrdinal(CookedFile file)
    {
        Match match = Regex.Match(GetLogicalMenuArtName(file), @"coach_?(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out int ordinal)
            ? ordinal
            : int.MaxValue;
    }

    private static bool IsPreMergedAudioFile(CookedFile file)
    {
        string relativePath = file.RelativePath.Replace('\\', '/');
        return relativePath.Contains("/media/", StringComparison.OrdinalIgnoreCase);
    }
}
