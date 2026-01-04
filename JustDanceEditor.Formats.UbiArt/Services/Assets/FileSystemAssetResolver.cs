using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Services.Assets;

public class FileSystemAssetResolver(IUbiArtLayout layout, FileSystem fileSystem) : IUbiArtAssetResolver
{
    private readonly IUbiArtLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly FileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private readonly string[] TextureExtensions = [".webp", ".png", ".tga", ".jpg", ".jpeg"];
    private readonly string[] AudioExtensions = [".ogg", ".wav", ".wem"];

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

    public CookedFile? FindPictogram(string name)
    {
        // Try direct absolute checks first (robust for flat uncooked layouts and simple tests)
        List<string> exts = [.. TextureExtensions];
        if (_fileSystem.ContainerStyle == UbiArtContainerStyle.Cooked)
        {
            var cooked = exts.Select(e => e + ".ckd").ToList();
            exts = [.. cooked, .. exts];
        }

        foreach (string ext in exts)
        {
            string candidate = Path.Combine(_fileSystem.ConversionRequest.InputPath, _fileSystem.InputFolders.PictosFolder, name + ext);
            if (File.Exists(candidate))
                return new CookedFile(candidate);
        }

        if (TryFindFileWithExtensions(_fileSystem.InputFolders.PictosFolder, name, TextureExtensions, out CookedFile? file))
            return file;

        // Try an absolute recursive search across the input path as a last resort
        try
        {
            string root = _fileSystem.ConversionRequest.InputPath;
            var matches = Directory.GetFiles(root, name + ".*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(root, name + ".*.ckd", SearchOption.AllDirectories)).ToArray();

            if (matches.Length > 0)
                return new CookedFile(matches[0]);
        }
        catch { }

        // Montage fallback: return first available file via filesystem
        try
        {
            CookedFile[] all = _fileSystem.GetAllFiles(_fileSystem.InputFolders.PictosFolder);
            return all.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public CookedFile? GetCoverArt()
    {
        string pattern = $"{_fileSystem.SongName}_cover_*";
        CookedFile[] files = _fileSystem.GetAllFiles(_fileSystem.InputFolders.MenuArtFolder, pattern);
        if (files.Length > 0)
            return files.OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).First();

        // Try more generic patterns
        files = _fileSystem.GetAllFiles(_fileSystem.InputFolders.MenuArtFolder, "*cover*");
        return files.FirstOrDefault();
    }

    public CookedFile[] GetCoachTextures()
    {
        CookedFile[] files = [.. _fileSystem.GetAllFiles(_fileSystem.InputFolders.MenuArtFolder, $"{_fileSystem.SongName}_coach_*").Where(f => !Path.GetFileName(f.FullPath).EndsWith("_phone", StringComparison.OrdinalIgnoreCase))];
        return files;
    }

    public CookedFile? GetBackgroundTexture()
    {
        CookedFile[] candidates = _fileSystem.GetAllFiles(_fileSystem.InputFolders.MenuArtFolder, $"{_fileSystem.SongName}_map_bkg.*");
        if (candidates.Length > 0)
            return candidates[0];

        candidates = _fileSystem.GetAllFiles(_fileSystem.InputFolders.MenuArtFolder, $"{_fileSystem.SongName}_banner_bkg.*");
        return candidates.FirstOrDefault();
    }

    public CookedFile[] GetMoveFiles()
    {
        string movesFolder = _fileSystem.InputFolders.MovesFolder;
        try
        {
            CookedFile[] files = _fileSystem.GetAllFiles(movesFolder, "*.msm");
            return files;
        }
        catch
        {
            return [];
        }
    }

    public string? FindFirstVideoFile()
    {
        if (_fileSystem.GetFolderPath(_fileSystem.InputFolders.MediaFolder, out _))
        {
            CookedFile[] mediaVideos = _fileSystem.GetAllFiles(_fileSystem.InputFolders.MediaFolder, "*.webm");
            if (mediaVideos.Length > 0)
                return mediaVideos[0];
        }

        CookedFile[] coachVideos = _fileSystem.GetAllFiles(Path.Combine(_fileSystem.InputFolders.MapWorldFolder, "videoscoach"), "*.webm");
        if (coachVideos.Length > 0)
            return coachVideos[0];

        return null;
    }

    public bool TryFindAudio(string relativePath, [NotNullWhen(true)] out CookedFile? file)
    {
        // Use the file system's existing resolution first
        if (_fileSystem.GetFilePath(relativePath, out file))
            return true;

        // Fallback via extensions
        string baseName = Path.GetFileNameWithoutExtension(relativePath);
        string folder = Path.GetDirectoryName(relativePath) ?? string.Empty;
        return TryFindFileWithExtensions(folder, baseName, AudioExtensions, out file);
    }

    public bool TryFindMainAudio(JDUbiArtSong songData, [NotNullWhen(true)] out CookedFile? file, out bool isPreMerged)
    {
        isPreMerged = false;

        // Pre-merged OGG in media folder
        if (_fileSystem.GetFolderPath(_fileSystem.InputFolders.MediaFolder, out _))
        {
            CookedFile[] oggFiles = _fileSystem.GetAllFiles(_fileSystem.InputFolders.MediaFolder, "*.ogg");
            if (oggFiles.Length > 0)
            {
                isPreMerged = true;
                file = oggFiles[0];
                return true;
            }
        }

        // Try the MusicTrack path
        string relativePath = songData.MusicTrack.COMPONENTS[0].trackData.path;
        return TryFindAudio(relativePath, out file);
    }

    public bool TryFindFileWithExtensions(string folderRelative, string baseName, IEnumerable<string> extensions, out CookedFile? file)
    {
        file = null;

        // Build ordered list of extensions to try, including cooked variants if necessary
        List<string> extList = [.. extensions];
        if (_fileSystem.ContainerStyle == UbiArtContainerStyle.Cooked)
        {
            // For each extension add extension + .ckd as higher priority
            List<string> cookedExts = [];
            foreach (var e in extList)
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
            string candidateAbsolute = Path.Combine(_fileSystem.ConversionRequest.InputPath, candidateRelative);
            if (File.Exists(candidateAbsolute))
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
                string fileName = Path.GetFileName(f.FullPath);
                string rootName = fileName.Split('.')[0]; // strip all extensions
                if (string.Equals(rootName, baseName, StringComparison.OrdinalIgnoreCase))
                {
                    file = f;
                    return true;
                }

                // Also consider names like baseName_suffix
                if (fileName.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase))
                {
                    file = f;
                    return true;
                }
            }
        }
        catch { }

        // As a final fallback, check absolute path for plain baseName (no extension)
        string fallbackAbsolute = Path.Combine(_fileSystem.ConversionRequest.InputPath, folderRelative, baseName);
        if (File.Exists(fallbackAbsolute))
        {
            file = new CookedFile(fallbackAbsolute);
            return true;
        }

        return false;
    }
}