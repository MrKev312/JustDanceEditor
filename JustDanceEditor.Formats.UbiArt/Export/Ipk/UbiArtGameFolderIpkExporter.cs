using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Ipk;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Export.Ipk;

using static UbiArtIpkOwnershipIndex;

internal sealed class UbiArtGameFolderIpkExporter
{
    private readonly ILogger _logger;
    private readonly List<UbiArtIpkArchiveIndex> _archives;
    private readonly Dictionary<string, List<UbiArtIpkArchiveIndex>> _exactOwners;
    private readonly Dictionary<string, List<UbiArtIpkArchiveIndex>> _patternOwners;
    private readonly UbiArtIpkArchiveIndex? _patchArchive;
    private readonly UbiArtIpkArchiveIndex? _primarySongBundle;
    private readonly bool _hasPerSongArchiveStyle;

    private UbiArtGameFolderIpkExporter(
        string selectedOutputFolder,
        string archiveFolder,
        UbiArtPlatform platform,
        ILogger logger,
        List<UbiArtIpkArchiveIndex> archives)
    {
        SelectedOutputFolder = selectedOutputFolder;
        ArchiveFolder = archiveFolder;
        Platform = platform;
        PlatformFolder = platform.GetCookedFolderName();
        _logger = logger;
        _archives = archives;
        _exactOwners = BuildExactOwners(archives);
        _patternOwners = BuildPatternOwners(archives);
        _patchArchive = archives.FirstOrDefault(archive => archive.IsPatch);
        _primarySongBundle = archives
            .Where(archive => archive.IsNumberedBundle)
            .OrderBy(archive => new FileInfo(archive.Path).Length)
            .ThenBy(archive => archive.FileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        _hasPerSongArchiveStyle = archives.Any(archive => archive.IsSongArchive);
    }

    public string SelectedOutputFolder { get; }
    public string ArchiveFolder { get; }
    public UbiArtPlatform Platform { get; }
    public string PlatformFolder { get; }

    public static bool LooksLikeGameFolder(string outputFolder, UbiArtPlatform platform)
        => TryFindArchiveFolder(outputFolder, platform, out _);

    public static bool TryCreate(string outputFolder, UbiArtPlatform platform, ILogger logger, out UbiArtGameFolderIpkExporter? exporter)
    {
        exporter = null;

        if (!TryFindArchiveFolder(outputFolder, platform, out string? archiveFolder) || archiveFolder is null)
            return false;

        string platformFolder = platform.GetCookedFolderName();
        List<string> archivePaths = [.. EnumeratePlatformArchives(archiveFolder, platformFolder)];
        if (archivePaths.Count == 0)
            return false;

        List<UbiArtIpkArchiveIndex> archives = [];
        foreach (string archivePath in archivePaths)
        {
            try
            {
                archives.Add(UbiArtIpkArchiveIndex.Read(archivePath));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping unreadable IPK archive '{ArchivePath}'.", archivePath);
            }
        }

        if (archives.Count == 0)
            return false;

        exporter = new UbiArtGameFolderIpkExporter(outputFolder, archiveFolder, platform, logger, archives);
        return true;
    }

    public Task ApplyAsync(
        string stagingFolder,
        IntermediateSongPackage package,
        UbiArtEngineVersion engineVersion = UbiArtEngineVersion.Unknown,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        cancellationToken.ThrowIfCancellationRequested();

        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        AddOrUpdateSkuSceneEntries(stagingFolder, mapName, mapNameLower);
        AddOrUpdateCarouselRulesEntries(stagingFolder, package, engineVersion);

        List<StagedFile> stagedFiles = [.. Directory
            .EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories)
            .Select(file => new StagedFile(file, UbiArtIpkArchiveIndex.NormalizePath(System.IO.Path.GetRelativePath(stagingFolder, file))))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)];

        if (stagedFiles.Count == 0)
        {
            _logger.LogInformation("No staged UbiArt files were produced; no IPKs were updated.");
            return Task.CompletedTask;
        }

        Dictionary<string, List<StagedFile>> assignments = new(StringComparer.OrdinalIgnoreCase);
        foreach (StagedFile stagedFile in stagedFiles)
        {
            foreach (string archivePath in ResolveArchivePaths(stagedFile.RelativePath, mapNameLower))
            {
                if (!assignments.TryGetValue(archivePath, out List<StagedFile>? archiveFiles))
                {
                    archiveFiles = [];
                    assignments[archivePath] = archiveFiles;
                }

                archiveFiles.Add(stagedFile);
            }
        }

        foreach ((string archivePath, List<StagedFile> files) in assignments.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RepackArchive(archivePath, files);
        }

        UbiArtSecureFatWriter.Update(ArchiveFolder, PlatformFolder, _logger);

        _logger.LogInformation(
            "Updated {ArchiveCount} IPK archive(s) in '{ArchiveFolder}' for {MapName}.",
            assignments.Count,
            ArchiveFolder,
            mapName);

        return Task.CompletedTask;
    }

    private static bool TryFindArchiveFolder(string outputFolder, UbiArtPlatform platform, out string? archiveFolder)
    {
        archiveFolder = null;
        if (platform == UbiArtPlatform.Uncooked || string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
            return false;

        string platformFolder = platform.GetCookedFolderName();
        if (EnumeratePlatformArchives(outputFolder, platformFolder).Any())
        {
            archiveFolder = outputFolder;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumeratePlatformArchives(string folder, string platformFolder)
    {
        if (!Directory.Exists(folder))
            yield break;

        string suffix = "_" + platformFolder;
        foreach (string path in Directory.EnumerateFiles(folder, "*.ipk", SearchOption.TopDirectoryOnly))
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                yield return path;
        }
    }

    private IEnumerable<string> ResolveArchivePaths(string relativePath, string mapNameLower)
    {
        string normalized = UbiArtIpkArchiveIndex.NormalizePath(relativePath);

        if (_exactOwners.TryGetValue(normalized, out List<UbiArtIpkArchiveIndex>? exactOwners))
            return SelectEffectiveExactOwners(exactOwners, normalized, mapNameLower).Select(archive => archive.Path);

        bool isCurrentMapAsset = IsCurrentMapAsset(normalized, mapNameLower);
        string pattern = UbiArtIpkArchiveIndex.ToSongPattern(normalized, mapNameLower);
        if (_patternOwners.TryGetValue(pattern, out List<UbiArtIpkArchiveIndex>? patternOwners))
        {
            List<UbiArtIpkArchiveIndex> sharedOwners = [.. patternOwners
                .Where(archive => archive.IsSharedBundle)
                .DistinctBy(archive => archive.Path, StringComparer.OrdinalIgnoreCase)];

            if (_hasPerSongArchiveStyle && isCurrentMapAsset && !ShouldUseSharedMapArchiveForPerSongGame(normalized, mapNameLower))
                return [ResolveSongContentArchivePath(mapNameLower)];

            if (sharedOwners.Count > 0)
                return [SelectDominantPatternOwner(sharedOwners, pattern).Path];

            return [ResolveSongContentArchivePath(mapNameLower)];
        }

        return [ResolveSongContentArchivePath(mapNameLower)];
    }

    private static bool IsCurrentMapAsset(string normalizedPath, string mapNameLower)
        => !string.IsNullOrWhiteSpace(mapNameLower) &&
            UbiArtIpkArchiveIndex.TryGetMapName(normalizedPath) is string mapName &&
            string.Equals(mapName, mapNameLower, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldUseSharedMapArchiveForPerSongGame(string normalizedPath, string mapNameLower)
    {
        string prefix = $"world/maps/{mapNameLower}/";
        int prefixIndex = normalizedPath.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex < 0)
            return false;

        string mapRelativePath = normalizedPath[(prefixIndex + prefix.Length)..];
        string fileName = System.IO.Path.GetFileName(mapRelativePath);

        if (string.Equals(mapRelativePath, "songdesc.tpl.ckd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mapRelativePath, "songdesc.act.ckd", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!mapRelativePath.StartsWith("menuart/actors/", StringComparison.OrdinalIgnoreCase) &&
            !mapRelativePath.StartsWith("menuart/textures/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fileName.StartsWith($"{mapNameLower}_cover_generic.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith($"{mapNameLower}_cover_online.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith($"{mapNameLower}_cover_phone.", StringComparison.OrdinalIgnoreCase) ||
            (fileName.StartsWith($"{mapNameLower}_coach_", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith("_phone.png", StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveSongContentArchivePath(string mapNameLower)
    {
        if (_hasPerSongArchiveStyle)
        {
            UbiArtIpkArchiveIndex? existingSongArchive = _archives.FirstOrDefault(archive =>
                archive.IsSongArchive &&
                archive.NameWithoutExtension.Contains(mapNameLower, StringComparison.OrdinalIgnoreCase));

            if (existingSongArchive is not null)
                return existingSongArchive.Path;

            return System.IO.Path.Combine(ArchiveFolder, $"{mapNameLower}_{PlatformFolder}.ipk");
        }

        if (_primarySongBundle is not null)
            return _primarySongBundle.Path;

        UbiArtIpkArchiveIndex? sharedBundle = _archives.FirstOrDefault(archive => archive.IsSharedBundle);
        if (sharedBundle is not null)
            return sharedBundle.Path;

        return System.IO.Path.Combine(ArchiveFolder, $"{mapNameLower}_{PlatformFolder}.ipk");
    }

    private void RepackArchive(string archivePath, IReadOnlyCollection<StagedFile> files)
    {
        string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jdi_ipk_" + Guid.NewGuid().ToString("N"));
        string extractFolder = System.IO.Path.Combine(tempRoot, "extract");
        string newArchivePath = System.IO.Path.Combine(tempRoot, System.IO.Path.GetFileName(archivePath));

        Directory.CreateDirectory(extractFolder);

        try
        {
            bool swapPathAndName = GetSwapPathAndNameForArchive(archivePath);
            if (File.Exists(archivePath))
            {
                UbiArtIpkArchiveMerger.Merge(
                    archivePath,
                    newArchivePath,
                    files.Select(file => new UbiArtIpkMergeFile(file.FullPath, file.RelativePath)).ToList(),
                    swapPathAndName);
            }
            else
            {
                foreach (StagedFile file in files)
                {
                    string destinationPath = System.IO.Path.Combine(extractFolder, file.RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath) ?? extractFolder);
                    File.Copy(file.FullPath, destinationPath, overwrite: true);
                }

                PackQuietly(extractFolder, newArchivePath, swapPathAndName);
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(archivePath) ?? ArchiveFolder);
            File.Copy(newArchivePath, archivePath, overwrite: true);

            string archiveName = System.IO.Path.GetFileName(archivePath);
            _logger.LogInformation("Packed {FileCount} file(s) into {ArchiveName}.", files.Count, archiveName);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete temporary IPK folder '{TempFolder}'.", tempRoot);
            }
        }
    }

    private bool GetSwapPathAndNameForArchive(string archivePath)
    {
        UbiArtIpkArchiveIndex? existing = _archives.FirstOrDefault(
            archive => string.Equals(archive.Path, archivePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing.SwapPathAndName;

        if (Platform == UbiArtPlatform.Cafe)
            return true;

        return _archives.Any(archive => archive.SwapPathAndName);
    }

    private static void PackQuietly(string inputFolder, string archivePath, bool swapPathAndName)
    {
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            new UbiArtIpkWriter(inputFolder, archivePath, swapPathAndName).Pack();
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    private void AddOrUpdateSkuSceneEntries(string stagingFolder, string mapName, string mapNameLower)
    {
        List<(UbiArtIpkArchiveIndex Archive, string Entry)> skuSceneEntries = FindEffectiveOwnedEntries(entry =>
            IsSkuScenePath(entry) && IsPlatformSkuScene(entry));

        if (skuSceneEntries.Count == 0)
        {
            _logger.LogWarning("No platform skuscene map database was found; {MapName} was exported without skuscene registration.", mapName);
            return;
        }

        UbiArtSkuScenePatchContext patchContext = UbiArtSkuScenePatchContext.FromStaging(stagingFolder, PlatformFolder, mapName, mapNameLower);

        foreach ((UbiArtIpkArchiveIndex archive, string entry) in skuSceneEntries)
        {
            try
            {
                byte[] originalBytes;
                using (UbiArtIpkFileSystem fileSystem = new(archive.Path))
                    originalBytes = fileSystem.ReadAllBytes(entry);

                if (!UbiArtSkuScenePatcher.TryPatch(originalBytes, patchContext, out byte[] updatedBytes))
                    continue;

                StagePatchedEntry(stagingFolder, entry, updatedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update skuscene '{SkuScenePath}' in '{ArchiveName}'.", entry, archive.FileName);
            }
        }
    }

    private void AddOrUpdateCarouselRulesEntries(
        string stagingFolder,
        IntermediateSongPackage package,
        UbiArtEngineVersion engineVersion)
    {
        if (!ShouldPatchCarouselRules(Platform, engineVersion))
            return;

        int originalVersion = checked((int)package.Metadata.OriginalJDVersion);
        if (originalVersion <= 0 || originalVersion <= (int)engineVersion)
            return;

        List<(UbiArtIpkArchiveIndex Archive, string Entry)> carouselRuleEntries = FindEffectiveOwnedEntries(IsCarouselRulesPath);

        if (carouselRuleEntries.Count == 0)
        {
            _logger.LogDebug(
                "No carousel rules were found for {Platform} {EngineVersion}; no version carousel was added.",
                Platform,
                engineVersion);
            return;
        }

        foreach ((UbiArtIpkArchiveIndex archive, string entry) in carouselRuleEntries)
        {
            try
            {
                byte[] originalBytes;
                using (UbiArtIpkFileSystem fileSystem = new(archive.Path))
                    originalBytes = fileSystem.ReadAllBytes(entry);

                if (!UbiArtCarouselRulesPatcher.TryPatch(originalBytes, originalVersion, out byte[] updatedBytes))
                    continue;

                StagePatchedEntry(stagingFolder, entry, updatedBytes);

                _logger.LogInformation(
                    "Added JD{OriginalVersion} carousel rules in '{CarouselRulesPath}' from '{ArchiveName}'.",
                    originalVersion,
                    entry,
                    archive.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update carousel rules '{CarouselRulesPath}' in '{ArchiveName}'.", entry, archive.FileName);
            }
        }
    }

    private List<(UbiArtIpkArchiveIndex Archive, string Entry)> FindEffectiveOwnedEntries(Func<string, bool> entryPredicate)
        => [.. _archives
            .SelectMany(archive => archive.Entries
                .Where(entryPredicate)
                .Select(entry => (Archive: archive, Entry: entry)))
            .GroupBy(pair => pair.Entry, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.FirstOrDefault(pair => pair.Archive.IsPatch, group.First()))];

    private static void StagePatchedEntry(string stagingFolder, string relativePath, byte[] bytes)
    {
        string outputPath = System.IO.Path.Combine(stagingFolder, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath) ?? stagingFolder);
        File.WriteAllBytes(outputPath, bytes);
    }

    private static bool IsSkuScenePath(string relativePath)
    {
        string normalized = UbiArtIpkArchiveIndex.NormalizePath(relativePath);
        return normalized.Contains("/skuscenes/skuscene_maps_", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith(".isc.ckd", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsPlatformSkuScene(string relativePath)
    {
        string fileName = System.IO.Path.GetFileName(UbiArtIpkArchiveIndex.NormalizePath(relativePath));
        if (fileName.Contains($"_{PlatformFolder}_", StringComparison.OrdinalIgnoreCase))
            return true;

        return Platform == UbiArtPlatform.NX &&
            fileName.Contains("_pc_", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCarouselRulesPath(string relativePath)
    {
        string normalized = UbiArtIpkArchiveIndex.NormalizePath(relativePath);
        return string.Equals(
            normalized,
            $"cache/itf_cooked/{PlatformFolder}/enginedata/gameconfig/gc_carousel_rules.json.ckd",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldPatchCarouselRules(UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => engineVersion is >= UbiArtEngineVersion.JD2016 and <= UbiArtEngineVersion.JD2018
            && platform is not (
                UbiArtPlatform.Uncooked or
                UbiArtPlatform.Revolution or
                UbiArtPlatform.Cell or
                UbiArtPlatform.Xenon);

    private sealed record StagedFile(string FullPath, string RelativePath);
}
