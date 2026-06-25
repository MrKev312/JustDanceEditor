using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import.Assets;

internal static class UbiArtVideoFileSelector
{
    public static CookedFile? FindPreferredVideoFile(JustDanceUbiArtFileSystem fileSystem)
        => FindPreferredVideoFiles(fileSystem).FirstOrDefault();

    public static CookedFile[] FindPreferredVideoFiles(JustDanceUbiArtFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        CookedFile[] physicalMediaFiles = FindPhysicalMediaFiles(fileSystem);
        if (physicalMediaFiles.Length > 0)
            return ChoosePreferredVideoFiles(physicalMediaFiles, fileSystem.SongName);

        if (fileSystem.GetFolderPath(fileSystem.InputFolders.MediaFolder, out _))
        {
            CookedFile[] mediaFiles = fileSystem.GetAllFiles(fileSystem.InputFolders.MediaFolder, "*.webm");
            if (mediaFiles.Length > 0)
                return ChoosePreferredVideoFiles(mediaFiles, fileSystem.SongName);
        }

        string videosCoachFolder = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "videoscoach");
        List<VideoCandidate> candidates = [];
        foreach (CookedFile file in fileSystem.GetAllFiles(videosCoachFolder, "*.webm"))
            candidates.Add(new(file, 0));

        return [.. ChoosePreferredVideoCandidates(candidates, fileSystem.SongName).Select(candidate => candidate.File)];
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

    private static CookedFile[] FindPhysicalMediaFiles(JustDanceUbiArtFileSystem fileSystem)
    {
        string mediaFolder = Path.Combine(fileSystem.ConversionRequest.InputPath, fileSystem.InputFolders.MediaFolder);
        if (!Directory.Exists(mediaFolder))
            return [];

        return
        [
            .. Directory.GetFiles(mediaFolder, "*.webm", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetRelativePath(fileSystem.ConversionRequest.InputPath, path).Replace('\\', Path.DirectorySeparatorChar))
                .Select(relative => new CookedFile(relative))
        ];
    }

    private static readonly string[] PreferredVideoQualities = ["ULTRA", "HIGH", "MID", "LOW"];

    private readonly record struct VideoCandidate(CookedFile File, int SourceRank);
}