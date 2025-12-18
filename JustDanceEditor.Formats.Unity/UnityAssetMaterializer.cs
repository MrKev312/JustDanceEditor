using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Globalization;

namespace JustDanceEditor.Formats.Unity;

public static class UnityAssetMaterializer
{

    private static readonly WebpEncoder LosslessWebpEncoder = new()
    {
        FileFormat = WebpFileFormatType.Lossless,
        Quality = 100
    };

    public static void Materialize(IntermediateSongPackage package, string unityRoot, string targetRoot)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(unityRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);

        string assetsRoot = ResolvePackagePath(targetRoot, IntermediatePackageLayout.Assets.Root);
        Directory.CreateDirectory(assetsRoot);
        Logger.Log($"Materializing Unity assets from '{unityRoot}' into '{assetsRoot}'.", LogLevel.Info);

        try
        {
            Logger.Log("Extracting assets in parallel...", LogLevel.Debug);
            
            // Parallelize all independent extraction operations
            Parallel.Invoke(
                () => CopyAudio(unityRoot, targetRoot),
                () => CopyVideo(unityRoot, targetRoot),
                () => ExtractBrandingAssets(unityRoot, targetRoot),
                () => ExtractCoachAssets(unityRoot, targetRoot),
                () => ExtractPictograms(unityRoot, targetRoot),
                () => ExtractMotionScripts(unityRoot, targetRoot),
                () => ExtractGestureFiles(unityRoot, targetRoot)
            );
        }
        catch (Exception ex)
        {
            Logger.Log($"Unity asset extraction failed: {ex.Message}", LogLevel.Error);
            throw;
        }

        Logger.Log("Unity asset extraction complete.", LogLevel.Info);
    }

    private static void CopyAudio(string unityRoot, string packageRoot)
    {
        string audioFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder);

        string masterDestination = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.AudioMasterFile);
        string masterSource = Path.Combine(unityRoot, "Audio_opus");
        string? masterPath = CopyFirstMatch(masterSource, "*.opus", audioFolder, Path.GetFileName(masterDestination));

        string previewDestination = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.AudioPreviewFile);
        string previewSource = Path.Combine(unityRoot, "AudioPreview_opus");
        string? previewPath = CopyFirstMatch(previewSource, "*.opus", audioFolder, Path.GetFileName(previewDestination));

        if (masterPath == null)
        {
            Logger.Log("No master audio track found in Unity export; 'master.opus' will be missing.", LogLevel.Warning);
            TryDeleteFile(masterDestination);
        }
        else
        {
            Logger.Log("Copied master audio track to assets/audio/master.opus.", LogLevel.Info);
        }
        if (previewPath == null)
        {
            Logger.Log("No preview audio track found in Unity export; 'preview.opus' will be missing.", LogLevel.Warning);
            TryDeleteFile(previewDestination);
        }
        else
        {
            Logger.Log("Copied preview audio track to assets/audio/preview.opus.", LogLevel.Info);
        }
    }

    private static void CopyVideo(string unityRoot, string packageRoot)
    {
        string backgroundSource = Path.Combine(unityRoot, "video");
        string backgroundDestination = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        int backgroundCount = CopyBackgroundVideos(backgroundSource, backgroundDestination);

        string previewSource = Path.Combine(unityRoot, "videoPreview");
        string previewDestination = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PreviewVideoFolder);
        int previewCount = CopyPreviewVideos(previewSource, previewDestination);

        if (backgroundCount == 0)
        {
            Logger.Log("No background video files found in Unity export.", LogLevel.Warning);
            TryDeleteDirectory(backgroundDestination);
        }
        else if (backgroundCount == 4)
        {
            Logger.Log("Detected source-of-truth background videos (4 variants). Copied all variants.", LogLevel.Info);
        }
        else
        {
            Logger.Log("Using single master background video from Unity export.", LogLevel.Info);
        }

        if (previewCount == 0)
        {
            Logger.Log("Preview videos missing or incomplete; they will be regenerated during export.", LogLevel.Warning);
            TryDeleteDirectory(previewDestination);
        }
        else
        {
            Logger.Log("Detected source-of-truth preview videos (4 variants). Copied all variants.", LogLevel.Info);
        }
    }

    private static void ExtractBrandingAssets(string unityRoot, string packageRoot)
    {
        EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.CoverAssetsFolder);

        string coverDest = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoverFile);
        string? coverPath = ExtractSingleImage(Path.Combine(unityRoot, "Cover"), coverDest);

        string logoDest = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.SongTitleFile);
        string? logoPath = ExtractSingleImage(Path.Combine(unityRoot, "songTitleLogo"), logoDest);

        if (coverPath == null)
        {
            Logger.Log("Unity export does not contain cover art; 'thumbnail.webp' will be empty.", LogLevel.Warning);
            TryDeleteFile(coverDest);
        }
        else
        {
            Logger.Log("Extracted cover art to assets/branding/thumbnail.webp.", LogLevel.Info);
        }
        if (logoPath == null)
        {
            Logger.Log("Unity export does not contain a song title logo; 'songTitleLogo.webp' will be empty.", LogLevel.Warning);
            TryDeleteFile(logoDest);
        }
        else
        {
            Logger.Log("Extracted song title logo to assets/branding/songTitleLogo.webp.", LogLevel.Info);
        }
    }

    private static void ExtractCoachAssets(string unityRoot, string packageRoot)
    {
        string coachFolder = Path.Combine(unityRoot, "CoachesLarge");
        if (!Directory.Exists(coachFolder))
        {
            Logger.Log("Unity export does not include coach textures; 'assets/coaches' will be empty.", LogLevel.Warning);
            TryDeleteDirectory(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder));
            return;
        }

        string coachesFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder);
        string backgroundDest = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesBackgroundFile);
        bool backgroundExported = false;
        int exportedCoaches = 0;

        ProcessBundleTextures(coachFolder, (name, image) =>
        {
            if (name.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase))
            {
                SaveAsWebp(image, backgroundDest);
                backgroundExported = true;
                return true;
            }

            if (TryParseCoachIndex(name, out int index))
            {
                string destination = Path.Combine(coachesFolder, $"coach_{index:D2}.webp");
                SaveAsWebp(image, destination);
                exportedCoaches++;
            }

            return true;
        });

        if (backgroundExported)
        {
            Logger.Log($"Saved coaches background to {backgroundDest}", LogLevel.Debug);
        }
        else
        {
            Logger.Log("Coach background image not found in Unity export.", LogLevel.Warning);
        }

        if (exportedCoaches == 0)
        {
            Logger.Log("No coach imagery decoded from Unity export.", LogLevel.Warning);
            TryDeleteDirectory(coachesFolder);
        }
        else
        {
            Logger.Log($"Extracted {exportedCoaches} coach image(s).", LogLevel.Info);
        }
    }

    private static void ExtractPictograms(string unityRoot, string packageRoot)
    {
        string mapPackageFolder = Path.Combine(unityRoot, "MapPackage");
        string? bundlePath = LocateFirstBundle(mapPackageFolder);
        if (bundlePath == null)
        {
            Logger.Log("No pictogram bundle found in Unity export.", LogLevel.Warning);
            TryDeleteDirectory(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.PictogramsFolder));
            return;
        }

        string pictogramsFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PictogramsFolder);
        AssetsManager manager = new();
        Dictionary<long, Image<Rgba32>> atlasImages = [];
        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(bundlePath, true);
            AssetsFileInstance assetsFile = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            AssetsFile afile = assetsFile.file;
            afile.GenerateQuickLookup();

            foreach (AssetFileInfo texInfo in afile.AssetInfos.Where(i => i.TypeId == (int)AssetClassID.Texture2D))
            {
                AssetTypeValueField baseField = manager.GetBaseField(assetsFile, texInfo);
                try
                {
                    Image<Rgba32> image = UnityTextureExtractor.ExtractTexture(manager, assetsFile, texInfo, baseField);
                    atlasImages[texInfo.PathId] = image;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to decode texture '{baseField["m_Name"].AsString}': {ex.Message}", LogLevel.Warning);
                }
            }

            Dictionary<string, SpriteInfo> sprites = LoadSprites(manager, assetsFile, afile);
            List<RenderEntry> renderEntries = LoadSpriteAtlasEntries(manager, assetsFile, afile);
            HashSet<string> exported = new(StringComparer.OrdinalIgnoreCase);

            foreach (RenderEntry entry in renderEntries)
            {
                if (!sprites.TryGetValue(entry.RenderDataKey, out SpriteInfo? sprite))
                    continue;
                if (!atlasImages.TryGetValue(entry.TexturePathId, out Image<Rgba32>? atlasCandidate))
                    continue;
                if (atlasCandidate == null)
                    continue;

                Image<Rgba32> atlas = atlasCandidate;
                Rectangle cropRect = entry.GetRectangle(atlas.Width, atlas.Height);
                cropRect = ClampRectangle(cropRect, atlas.Width, atlas.Height);
                if (cropRect.Width <= 0 || cropRect.Height <= 0)
                    continue;

                string safeName = SanitizeFileName(sprite.Name);
                if (string.IsNullOrEmpty(safeName) || !exported.Add(safeName))
                    continue;

                string destination = Path.Combine(pictogramsFolder, $"{safeName}.webp");
                using Image<Rgba32> cropped = atlas.Clone(ctx => ctx.Crop(cropRect));
                SaveAsWebp(cropped, destination);
            }

            if (exported.Count == 0)
            {
                Logger.Log("No pictograms could be decoded from Unity export.", LogLevel.Warning);
                TryDeleteDirectory(pictogramsFolder);
            }
            else
            {
                Logger.Log($"Extracted {exported.Count} pictogram(s).", LogLevel.Info);
            }
        }
        finally
        {
            foreach (Image<Rgba32> image in atlasImages.Values)
                image.Dispose();
            manager.UnloadAll();
        }
    }

    private static void ExtractMotionScripts(string unityRoot, string packageRoot)
    {
        string mapPackageFolder = Path.Combine(unityRoot, "MapPackage");
        int exported = ExtractTextAssetsFromMapPackage(
            mapPackageFolder,
            EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.MovesFolder),
            name => name.EndsWith(".msm", StringComparison.OrdinalIgnoreCase),
            ".msm");

        if (exported == 0)
        {
            Logger.Log("No motion scripts (*.msm) were exported from the Unity map package.", LogLevel.Warning);
            TryDeleteDirectory(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.MovesFolder));
        }
        else
        {
            Logger.Log($"Extracted {exported} motion script(s).", LogLevel.Info);
        }
    }

    private static void ExtractGestureFiles(string unityRoot, string packageRoot)
    {
        string mapPackageFolder = Path.Combine(unityRoot, "MapPackage");
        int exported = ExtractTextAssetsFromMapPackage(
            mapPackageFolder,
            EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.GesturesFolder),
            name => name.EndsWith(".gesture", StringComparison.OrdinalIgnoreCase),
            ".gesture");

        if (exported == 0)
        {
            Logger.Log("No gesture assets (*.gesture) were exported from the Unity map package.", LogLevel.Warning);
            TryDeleteDirectory(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.GesturesFolder));
        }
        else
        {
            Logger.Log($"Extracted {exported} gesture asset(s).", LogLevel.Info);
        }
    }

    private static string? CopyFirstMatch(string sourceFolder, string searchPattern, string destinationFolder, string destinationFileName)
    {
        if (!Directory.Exists(sourceFolder))
            return null;

        string? source = Directory.EnumerateFiles(sourceFolder, searchPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (source == null)
            return null;

        Directory.CreateDirectory(destinationFolder);
        string destination = Path.Combine(destinationFolder, destinationFileName);
        File.Copy(source, destination, true);
        return destination;
    }

    private static int CopyBackgroundVideos(string sourceFolder, string destinationFolder)
    {
        string[] sources = GetVideoFiles(sourceFolder);
        if (sources.Length == 0)
            return 0;

        Directory.CreateDirectory(destinationFolder);

        if (sources.Length == 4)
        {
            foreach (string source in sources)
            {
                string destination = Path.Combine(destinationFolder, Path.GetFileName(source));
                File.Copy(source, destination, true);
            }

            return 4;
        }

        string largest = sources.OrderByDescending(f => new FileInfo(f).Length).First();
        string masterPath = Path.Combine(destinationFolder, "master.webm");
        File.Copy(largest, masterPath, true);
        return 1;
    }

    private static int CopyPreviewVideos(string sourceFolder, string destinationFolder)
    {
        string[] sources = GetVideoFiles(sourceFolder);
        if (sources.Length == 0)
            return 0;

        if (sources.Length != 4)
            return 0;

        Directory.CreateDirectory(destinationFolder);
        foreach (string source in sources)
        {
            string destination = Path.Combine(destinationFolder, Path.GetFileName(source));
            File.Copy(source, destination, true);
        }

        return 4;
    }

    private static string[] GetVideoFiles(string sourceFolder)
    {
        if (!Directory.Exists(sourceFolder))
            return Array.Empty<string>();

        string[] allowedExtensions = [".webm", ".mp4", ".mkv", ".mov"];
        return [.. Directory.EnumerateFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)];
    }

    private static string? ExtractSingleImage(string sourceFolder, string destinationFile)
    {
        if (!Directory.Exists(sourceFolder))
            return null;

        if (ProcessBundleTextures(sourceFolder, (_, image) =>
            {
                SaveAsWebp(image, destinationFile);
                return false;
            }))
            return destinationFile;

        string? rasterSource = Directory.EnumerateFiles(sourceFolder)
            .FirstOrDefault(HasImageExtension);
        if (rasterSource == null)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        using Image<Rgba32> image = Image.Load<Rgba32>(rasterSource);
        SaveAsWebp(image, destinationFile);
        return destinationFile;
    }

    private static bool ProcessBundleTextures(string folder, Func<string, Image<Rgba32>, bool> handler)
    {
        string? bundlePath = LocateFirstBundle(folder);
        if (bundlePath == null)
            return false;

        AssetsManager manager = new();
        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(bundlePath, true);
            AssetsFileInstance assetsFile = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            AssetsFile afile = assetsFile.file;
            afile.GenerateQuickLookup();

            foreach (AssetFileInfo textureInfo in afile.AssetInfos.Where(i => i.TypeId == (int)AssetClassID.Texture2D))
            {
                AssetTypeValueField baseField = manager.GetBaseField(assetsFile, textureInfo);
                using Image<Rgba32> image = UnityTextureExtractor.ExtractTexture(manager, assetsFile, textureInfo, baseField);
                bool shouldContinue = handler(baseField["m_Name"].AsString, image);
                if (!shouldContinue)
                    break;
            }
        }
        finally
        {
            manager.UnloadAll();
        }

        return true;
    }

    private static int ExtractTextAssetsFromMapPackage(
        string mapPackageFolder,
        string destinationFolder,
        Func<string, bool> filter,
        string defaultExtension)
    {
        string? bundlePath = LocateFirstBundle(mapPackageFolder);
        if (bundlePath == null)
            return 0;

        AssetsManager manager = new();
        HashSet<string> exportedNames = new(StringComparer.OrdinalIgnoreCase);
        int exported = 0;

        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(bundlePath, true);
            AssetsFileInstance assetsFile = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            AssetsFile afile = assetsFile.file;
            afile.GenerateQuickLookup();

            foreach (AssetFileInfo textInfo in afile.AssetInfos.Where(i => i.TypeId == (int)AssetClassID.TextAsset))
            {
                AssetTypeValueField baseField = manager.GetBaseField(assetsFile, textInfo);
                string assetName = baseField["m_Name"].AsString;
                if (!filter(assetName))
                    continue;

                byte[] data = ExtractTextAssetBytes(baseField);
                if (data.Length == 0)
                    continue;

                Directory.CreateDirectory(destinationFolder);
                string safeName = SanitizeFileName(assetName);
                if (string.IsNullOrEmpty(Path.GetExtension(safeName)) && !string.IsNullOrEmpty(defaultExtension))
                    safeName += defaultExtension;

                safeName = EnsureUniqueFileName(safeName, exportedNames);
                string destination = Path.Combine(destinationFolder, safeName);
                File.WriteAllBytes(destination, data);
                exported++;
            }
        }
        finally
        {
            manager.UnloadAll();
        }

        if (exported == 0)
            TryDeleteDirectory(destinationFolder);

        return exported;
    }

    private static string? LocateFirstBundle(string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        string? bundle = Directory.EnumerateFiles(folder, "*.bundle", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return bundle ?? Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void SaveAsWebp(Image<Rgba32> image, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        image.Save(destination, LosslessWebpEncoder);
    }

    private static byte[] ExtractTextAssetBytes(AssetTypeValueField baseField)
    {
        AssetTypeValueField scriptField = baseField["m_Script"];
        if (!scriptField.IsDummy && scriptField.Value?.ValueType == AssetValueType.String)
            return scriptField.AsByteArray;
        return [];
    }

    private static bool TryParseCoachIndex(string name, out int index)
    {
        index = 0;
        int marker = name.LastIndexOf("_Coach_", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return false;

        string suffix = name[(marker + 7)..];
        return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    private static Dictionary<string, SpriteInfo> LoadSprites(AssetsManager manager, AssetsFileInstance assetsFile, AssetsFile afile)
    {
        Dictionary<string, SpriteInfo> sprites = new(StringComparer.OrdinalIgnoreCase);
        foreach (AssetFileInfo spriteInfo in afile.AssetInfos.Where(i => i.TypeId == (int)AssetClassID.Sprite))
        {
            AssetTypeValueField baseField = manager.GetBaseField(assetsFile, spriteInfo);
            AssetTypeValueField keyField = baseField["m_RenderDataKey"]["first"];
            string key = ComposeRenderKey(keyField);
            if (!sprites.ContainsKey(key))
            {
                sprites[key] = new SpriteInfo(baseField["m_Name"].AsString);
            }
        }

        return sprites;
    }

    private static List<RenderEntry> LoadSpriteAtlasEntries(AssetsManager manager, AssetsFileInstance assetsFile, AssetsFile afile)
    {
        List<RenderEntry> entries = [];
        foreach (AssetFileInfo atlasInfo in afile.AssetInfos.Where(i => i.TypeId == (int)AssetClassID.SpriteAtlas))
        {
            AssetTypeValueField atlasBase = manager.GetBaseField(assetsFile, atlasInfo);
            AssetTypeValueField mapArray = atlasBase["m_RenderDataMap"]["Array"];
            foreach (AssetTypeValueField entry in mapArray.Children)
            {
                string key = ComposeRenderKey(entry["first"]["first"]);
                long textureId = entry["second"]["texture"]["m_PathID"].AsLong;
                AssetTypeValueField rect = entry["second"]["textureRect"];
                entries.Add(new RenderEntry(key,
                    textureId,
                    rect["x"].AsFloat,
                    rect["y"].AsFloat,
                    rect["width"].AsFloat,
                    rect["height"].AsFloat));
            }
        }

        return entries;
    }

    private static Rectangle ClampRectangle(Rectangle rect, int width, int height)
    {
        int x = Math.Clamp(rect.X, 0, width);
        int y = Math.Clamp(rect.Y, 0, height);
        int availableWidth = Math.Max(0, width - x);
        int availableHeight = Math.Max(0, height - y);
        int clampedWidth = Math.Clamp(rect.Width, 0, availableWidth);
        int clampedHeight = Math.Clamp(rect.Height, 0, availableHeight);
        return new Rectangle(x, y, clampedWidth, clampedHeight);
    }

    private static string ComposeRenderKey(AssetTypeValueField keyField)
    {
        uint[] parts = new uint[4];
        for (int i = 0; i < 4; i++)
            parts[i] = keyField[$"data[{i}]"]
                .AsUInt;
        return string.Concat(
            parts[0].ToString("X8", CultureInfo.InvariantCulture),
            parts[1].ToString("X8", CultureInfo.InvariantCulture),
            parts[2].ToString("X8", CultureInfo.InvariantCulture),
            parts[3].ToString("X8", CultureInfo.InvariantCulture));
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private static string EnsureUniqueFileName(string fileName, HashSet<string> existingNames)
    {
        if (existingNames.Add(fileName))
            return fileName;

        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        int counter = 1;
        string candidate;
        do
        {
            candidate = $"{nameWithoutExtension}_{counter++}{extension}";
        } while (!existingNames.Add(candidate));

        return candidate;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;

            if (Directory.EnumerateFileSystemEntries(folder).Any())
                return;

            Directory.Delete(folder);
        }
        catch
        {
            // Best-effort cleanup; leftover empty directories are harmless.
        }
    }

    private static string ResolvePackagePath(string packageRoot, string relativePath)
    {
        return IntermediatePackageLayout.Resolve(packageRoot, relativePath);
    }

    private static string EnsureFolder(string packageRoot, string relativeFolder)
    {
        string path = ResolvePackagePath(packageRoot, relativeFolder);
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool HasImageExtension(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp";
    }

    private sealed record SpriteInfo(string Name);

    private sealed record RenderEntry(string RenderDataKey, long TexturePathId, float X, float Y, float Width, float Height)
    {
        public Rectangle GetRectangle(int textureWidth, int textureHeight)
        {
            float x = X;
            float y = Y;
            float width = Width;
            float height = Height;

            if (IsNormalizedRectangle(x, y, width, height))
            {
                x *= textureWidth;
                y *= textureHeight;
                width *= textureWidth;
                height *= textureHeight;
            }

            int rectX = (int)Math.Floor(x);
            int bottomLeftY = (int)Math.Floor(y);
            int rectWidth = (int)Math.Ceiling(width);
            int rectHeight = (int)Math.Ceiling(height);

            int topLeftY = textureHeight - bottomLeftY - rectHeight;
            return new Rectangle(rectX, topLeftY, rectWidth, rectHeight);
        }

        private static bool IsNormalizedRectangle(float x, float y, float width, float height)
        {
            return width > 0f && height > 0f &&
                   width <= 1.1f && height <= 1.1f &&
                   x >= 0f && y >= 0f &&
                   x <= 1.1f && y <= 1.1f;
        }
    }
}