using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import.Assets;

internal static class UbiArtVideoFileSelector
{
    public static CookedFile? FindPreferredVideoFile(
        JustDanceUbiArtFileSystem fileSystem,
        bool allowContentSongFallback = true)
        => FindPreferredVideoFiles(fileSystem, allowContentSongFallback).FirstOrDefault();

    public static CookedFile[] FindPreferredVideoFiles(
        JustDanceUbiArtFileSystem fileSystem,
        bool allowContentSongFallback = true)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        foreach ((string MediaFolder, string SongName) in EnumerateMediaSources(fileSystem, allowContentSongFallback))
        {
            CookedFile[] physicalMediaFiles = FindPhysicalMediaFiles(fileSystem, MediaFolder);
            if (physicalMediaFiles.Length > 0)
                return ChoosePreferredVideoFiles(physicalMediaFiles, SongName);

            if (!fileSystem.GetFolderPath(MediaFolder, out _))
                continue;

            CookedFile[] mediaFiles = fileSystem.GetAllFiles(MediaFolder, "*.webm");
            if (mediaFiles.Length > 0)
                return ChoosePreferredVideoFiles(mediaFiles, SongName);
        }

        foreach ((string MapWorldFolder, string SongName) in EnumerateMapSources(fileSystem, allowContentSongFallback))
        {
            string videosCoachFolder = Path.Combine(MapWorldFolder, "videoscoach");
            List<VideoCandidate> candidates = [];
            foreach (CookedFile file in fileSystem.GetAllFiles(videosCoachFolder, "*.webm"))
                candidates.Add(new(file, 0));

            VideoCandidate[] preferred = [.. ChoosePreferredVideoCandidates(candidates, SongName)];
            if (preferred.Length > 0)
                return [.. preferred.Select(candidate => candidate.File)];
        }

        return [];
    }

    internal static CookedFile? ChoosePreferredVideoFile(IEnumerable<CookedFile> files, string songName) =>
        ChoosePreferredVideoFile(files.Select(file => new VideoCandidate(file, 0)), songName);

    internal static CookedFile[] ChoosePreferredVideoFiles(IEnumerable<CookedFile> files, string songName) =>
        [.. ChoosePreferredVideoCandidates(files.Select(file => new VideoCandidate(file, 0)), songName).Select(candidate => candidate.File)];

    private static CookedFile? ChoosePreferredVideoFile(IEnumerable<VideoCandidate> candidates, string songName)
    {
        string songNameLower = songName.ToLowerInvariant();

        return OrderVideoCandidates(candidates, songNameLower)
            .Select(candidate => candidate.File)
            .FirstOrDefault();
    }

    private static IEnumerable<VideoCandidate> ChoosePreferredVideoCandidates(IEnumerable<VideoCandidate> candidates, string songName)
    {
        string songNameLower = songName.ToLowerInvariant();
        VideoCandidate[] ordered = [.. OrderVideoCandidates(candidates, songNameLower)];
        VideoCandidate[] exactNonAlphaSongVideos =
        [
            .. ordered.Where(candidate => IsExactSongVideo(candidate.File, songNameLower) && !IsAlphaVideo(candidate.File))
        ];
        if (exactNonAlphaSongVideos.Length > 0)
            return exactNonAlphaSongVideos;

        if (ordered.Length <= 4)
            return ordered;

        List<VideoCandidate> preferred = [];
        foreach (string quality in PreferredVideoQualities)
        {
            VideoCandidate[] matches =
            [
                .. ordered
                .Where(candidate => IsQualityVideo(candidate.File, quality))
                .OrderBy(candidate => GetSpecificQualityRank(candidate.File, quality))
                .ThenBy(candidate => candidate.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            ];

            if (matches.Length > 0 &&
                preferred.All(candidate => !string.Equals(candidate.File.RelativePath, matches[0].File.RelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                preferred.Add(matches[0]);
            }
        }

        return preferred.Count > 0
            ? preferred
            : ordered.Take(1);
    }

    private static IEnumerable<VideoCandidate> OrderVideoCandidates(IEnumerable<VideoCandidate> candidates, string songNameLower)
    {
        return candidates
            .OrderBy(candidate => GetVideoRank(candidate.File, songNameLower))
            .ThenBy(candidate => GetQualityRank(candidate.File))
            .ThenBy(candidate => candidate.SourceRank)
            .ThenBy(candidate => candidate.File.RelativePath, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetVideoRank(CookedFile file, string songNameLower)
    {
        string name = Path.GetFileNameWithoutExtension(file.RelativePath).ToLowerInvariant();
        if (IsExactSongVideo(file, songNameLower))
        {
            return 0;
        }

        bool isAlpha = IsAlphaVideoName(name);
        bool isPreview = name.Contains("preview", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(songNameLower) &&
            name.StartsWith(songNameLower, StringComparison.OrdinalIgnoreCase) &&
            !isAlpha &&
            !isPreview)
        {
            return 10;
        }

        if (isAlpha)
            return 80;

        if (isPreview)
            return 90;

        return 50;
    }

    private static bool IsExactSongVideo(CookedFile file, string songNameLower)
    {
        string name = Path.GetFileNameWithoutExtension(file.RelativePath).ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(songNameLower) &&
            string.Equals(name, songNameLower, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlphaVideo(CookedFile file) =>
        IsAlphaVideoName(Path.GetFileNameWithoutExtension(file.RelativePath));

    private static bool IsAlphaVideoName(string name) =>
        name.EndsWith("_alpha", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".alpha", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("_alpha_", StringComparison.OrdinalIgnoreCase);

    private static bool IsQualityVideo(CookedFile file, string quality)
    {
        string name = Path.GetFileNameWithoutExtension(file.RelativePath);
        return name.Contains($"_{quality}.", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith($"_{quality}", StringComparison.OrdinalIgnoreCase) ||
            name.Contains($"_{quality}_", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetQualityRank(CookedFile file)
    {
        for (int i = 0; i < PreferredVideoQualities.Length; i++)
        {
            if (IsQualityVideo(file, PreferredVideoQualities[i]))
                return i;
        }

        return PreferredVideoQualities.Length;
    }

    private static int GetSpecificQualityRank(CookedFile file, string quality)
    {
        string name = Path.GetFileName(file.RelativePath);
        if (name.EndsWith($"_{quality}.hd.webm", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.EndsWith($"_{quality}.webm", StringComparison.OrdinalIgnoreCase))
            return 1;

        return 2;
    }

    private static CookedFile[] FindPhysicalMediaFiles(
        JustDanceUbiArtFileSystem fileSystem,
        string mediaFolder)
    {
        string physicalMediaFolder = Path.Combine(fileSystem.ConversionRequest.InputPath, mediaFolder);
        if (!Directory.Exists(physicalMediaFolder))
            return [];

        return
        [
            .. Directory.GetFiles(physicalMediaFolder, "*.webm", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetRelativePath(fileSystem.ConversionRequest.InputPath, path).Replace('\\', Path.DirectorySeparatorChar))
                .Select(relative => new CookedFile(relative))
        ];
    }

    private static IEnumerable<(string MediaFolder, string SongName)> EnumerateMediaSources(
        JustDanceUbiArtFileSystem fileSystem,
        bool allowContentSongFallback) =>
        allowContentSongFallback
            ? EnumerateDistinctSources(
                (fileSystem.InputFolders.SelectedMediaFolder, fileSystem.SongName),
                (fileSystem.InputFolders.MediaFolder, fileSystem.ContentSongName))
            : EnumerateDistinctSources(
                (fileSystem.InputFolders.SelectedMediaFolder, fileSystem.SongName));

    private static IEnumerable<(string MapWorldFolder, string SongName)> EnumerateMapSources(
        JustDanceUbiArtFileSystem fileSystem,
        bool allowContentSongFallback) =>
        allowContentSongFallback
            ? EnumerateDistinctSources(
                (fileSystem.InputFolders.SelectedMapWorldFolder, fileSystem.SongName),
                (fileSystem.InputFolders.MapWorldFolder, fileSystem.ContentSongName))
            : EnumerateDistinctSources(
                (fileSystem.InputFolders.SelectedMapWorldFolder, fileSystem.SongName));

    private static IEnumerable<(string Folder, string SongName)> EnumerateDistinctSources(
        params (string Folder, string SongName)[] sources)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string folder, string songName) in sources)
        {
            if (!string.IsNullOrWhiteSpace(folder) && seen.Add(folder))
                yield return (folder, string.IsNullOrWhiteSpace(songName) ? string.Empty : songName);
        }
    }

    private static readonly string[] PreferredVideoQualities = ["ULTRA", "HIGH", "MID", "LOW"];

    private readonly record struct VideoCandidate(CookedFile File, int SourceRank);
}
