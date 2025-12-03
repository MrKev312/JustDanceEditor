using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Assets;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Globalization;

namespace JustDanceEditor.Formats.Unity;

public static class UnityAssetMaterializer
{
    private const string MotionScriptsRole = "motion/msm";
    private const string GestureScriptsRole = "motion/gestures";

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

        AssetDirectories dirs = AssetDirectories.Create(targetRoot);
        Directory.CreateDirectory(dirs.Root);

        try
        {
            CopyAudio(package.AssetCatalog, unityRoot, dirs);
            CopyVideo(package.AssetCatalog, unityRoot, dirs);
            ExtractBrandingAssets(package.AssetCatalog, unityRoot, dirs);
            ExtractCoachAssets(package.AssetCatalog, unityRoot, dirs);
            ExtractPictograms(package.AssetCatalog, unityRoot, dirs);
            ExtractMotionScripts(package.AssetCatalog, unityRoot, dirs);
            ExtractGestureFiles(package.AssetCatalog, unityRoot, dirs);
        }
        catch (Exception ex)
        {
            Logger.Log($"Unity asset extraction failed: {ex.Message}", LogLevel.Error);
            throw;
        }

        CleanupCatalog(package.AssetCatalog);
    }

    private static void CopyAudio(IntermediateAssetCatalog catalog, string unityRoot, AssetDirectories dirs)
    {
        string masterSource = Path.Combine(unityRoot, "Audio_opus");
        string? masterPath = CopyFirstMatch(masterSource, "*.opus", dirs.Audio, "master.opus");
        UpdateAssetReference(catalog, "audio/master", masterPath, dirs);

        string previewSource = Path.Combine(unityRoot, "AudioPreview_opus");
        string? previewPath = CopyFirstMatch(previewSource, "*.opus", dirs.Audio, "preview.opus");
        UpdateAssetReference(catalog, "audio/preview", previewPath, dirs);
    }

    private static void CopyVideo(IntermediateAssetCatalog catalog, string unityRoot, AssetDirectories dirs)
    {
        string videoSource = Path.Combine(unityRoot, "video");
        bool hasBackground = CopyAllVideoVariants(videoSource, dirs.Video);
        UpdateAssetReference(catalog, "video/background", hasBackground ? dirs.Video : null, dirs, treatAsDirectory: true);

        string previewSource = Path.Combine(unityRoot, "videoPreview");
        bool hasPreview = CopyAllVideoVariants(previewSource, dirs.PreviewVideo);
        UpdateAssetReference(catalog, "video/preview", hasPreview ? dirs.PreviewVideo : null, dirs, treatAsDirectory: true);
    }

    private static void ExtractBrandingAssets(IntermediateAssetCatalog catalog, string unityRoot, AssetDirectories dirs)
    {
        Directory.CreateDirectory(dirs.Branding);

        string coverDest = Path.Combine(dirs.Branding, "thumbnail.webp");
        string? coverPath = ExtractSingleImage(Path.Combine(unityRoot, "Cover"), coverDest);
        UpdateAssetReference(catalog, "image/cover", coverPath, dirs, mimeType: "image/webp");

        string logoDest = Path.Combine(dirs.Branding, "songTitleLogo.webp");
        string? logoPath = ExtractSingleImage(Path.Combine(unityRoot, "songTitleLogo"), logoDest);
        UpdateAssetReference(catalog, "image/songTitleLogo", logoPath, dirs, mimeType: "image/webp");
    }

    private static void ExtractCoachAssets(IntermediateAssetCatalog catalog, string unityRoot, AssetDirectories dirs)
    {
        string coachFolder = Path.Combine(unityRoot, "CoachesLarge");
        if (!Directory.Exists(coachFolder))
        {
            UpdateAssetReference(catalog, "image/coachLarge", null, dirs);
            return;
        }

        Directory.CreateDirectory(dirs.Coaches);
        string backgroundDest = Path.Combine(dirs.Coaches, "coachesBackground.webp");
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
                string destination = Path.Combine(dirs.Coaches, $"coach_{index:D2}.webp");
                SaveAsWebp(image, destination);
                exportedCoaches++;
            }

            return true;
        });

        if (backgroundExported)
        {
            Logger.Log($"Saved coaches background to {backgroundDest}", LogLevel.Debug);
        }

        if (exportedCoaches > 0)
        {
            Directory.CreateDirectory(dirs.Coaches);
            UpdateAssetReference(catalog, "image/coachLarge", dirs.Coaches, dirs, treatAsDirectory: true, mimeType: "image/webp");
        }
        else
        {
            UpdateAssetReference(catalog, "image/coachLarge", null, dirs);
        }
    }

    private static void ExtractPictograms(IntermediateAssetCatalog catalog, string unityRoot, AssetDirectories dirs)
    {
        string mapPackageFolder = Path.Combine(unityRoot, "MapPackage");
        string? bundlePath = LocateFirstBundle(mapPackageFolder);
        if (bundlePath == null)
        {
            UpdateAssetReference(catalog, "atlas/pictograms", null, dirs);
            return;
        }

        Directory.CreateDirectory(dirs.Pictograms);
        AssetsManager manager = new();
        Dictionary<long, Image<Rgba32>> atlasImages = new();
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

                string destination = Path.Combine(dirs.Pictograms, $"{safeName}.webp");
                using Image<Rgba32> cropped = atlas.Clone(ctx => ctx.Crop(cropRect));
                SaveAsWebp(cropped, destination);
            }

            if (exported.Count > 0)
            {
                UpdateAssetReference(catalog, "atlas/pictograms", dirs.Pictograms, dirs, treatAsDirectory: true, mimeType: "image/webp");
            }
            else
            {
                UpdateAssetReference(catalog, "atlas/pictograms", null, dirs);
            }
        }
        finally
        {
            foreach (Image<Rgba32> image in atlasImages.Values)
                image.Dispose();
            manager.UnloadAll();
        }
    }

    private static void ExtractMotionScripts(IntermediateAssetCatalog catalog, string unityRoot, AssetDirectories dirs)
    {
        EnsureAssetRole(catalog, MotionScriptsRole);
        string mapPackageFolder = Path.Combine(unityRoot, "MapPackage");
        int exported = ExtractTextAssetsFromMapPackage(
            mapPackageFolder,
            dirs.Moves,
            name => name.EndsWith(".msm", StringComparison.OrdinalIgnoreCase),
            ".msm");

        string? referencePath = exported > 0 ? dirs.Moves : null;
        UpdateAssetReference(catalog, MotionScriptsRole, referencePath, dirs, treatAsDirectory: true);
    }

    private static void ExtractGestureFiles(IntermediateAssetCatalog catalog, string unityRoot, AssetDirectories dirs)
    {
        EnsureAssetRole(catalog, GestureScriptsRole);
        string mapPackageFolder = Path.Combine(unityRoot, "MapPackage");
        int exported = ExtractTextAssetsFromMapPackage(
            mapPackageFolder,
            dirs.Gestures,
            name => name.EndsWith(".gesture", StringComparison.OrdinalIgnoreCase),
            ".gesture");

        string? referencePath = exported > 0 ? dirs.Gestures : null;
        UpdateAssetReference(catalog, GestureScriptsRole, referencePath, dirs, treatAsDirectory: true);
    }

    private static void CleanupCatalog(IntermediateAssetCatalog catalog)
    {
        catalog.Assets.RemoveAll(asset => asset.Role.StartsWith("bundle/", StringComparison.OrdinalIgnoreCase));
        foreach (IntermediateAsset asset in catalog.Assets)
        {
            asset.Attributes.Clear();
            if (!string.IsNullOrWhiteSpace(asset.SourcePath))
                asset.SourcePath = NormalizePath(asset.SourcePath);
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

    private static bool CopyAllVideoVariants(string sourceFolder, string destinationFolder)
    {
        if (!Directory.Exists(sourceFolder))
            return false;

        string[] allowedExtensions = [".webm", ".mp4", ".mkv", ".mov"];
        List<string> sources = Directory.EnumerateFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sources.Count == 0)
            return false;

        Directory.CreateDirectory(destinationFolder);
        foreach (string source in sources)
        {
            string destination = Path.Combine(destinationFolder, Path.GetFileName(source));
            File.Copy(source, destination, true);
        }

        return true;
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
        List<RenderEntry> entries = new();
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

    private static void EnsureAssetRole(IntermediateAssetCatalog catalog, string role)
    {
        if (catalog.Assets.Any(asset => asset.Role == role))
            return;

        IntermediateAsset newAsset = catalog.Add(role);
        newAsset.Required = false;
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

    private static bool HasImageExtension(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp";
    }

    private static void UpdateAssetReference(IntermediateAssetCatalog catalog, string role, string? absolutePath, AssetDirectories dirs, string? mimeType = null, bool treatAsDirectory = false)
    {
        IntermediateAsset? asset = catalog.Assets.FirstOrDefault(a => a.Role == role);
        if (asset == null)
            return;

        if (absolutePath == null)
        {
            asset.Required = false;
            asset.SourcePath = null;
            asset.SizeBytes = null;
            asset.MimeType = null;
            return;
        }

        string relativePath = dirs.ToRelative(absolutePath);
        asset.SourcePath = relativePath;
        asset.SizeBytes = treatAsDirectory
            ? (Directory.Exists(absolutePath) ? Directory.EnumerateFiles(absolutePath, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : null)
            : new FileInfo(absolutePath).Length;
        asset.MimeType = mimeType;
        asset.Required = true;
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

    private sealed class AssetDirectories
    {
        public string TargetRoot { get; }
        public string Root { get; }
        public string Audio { get; }
        public string Video { get; }
        public string PreviewVideo { get; }
        public string Coaches { get; }
        public string Pictograms { get; }
        public string Branding { get; }
        public string Moves { get; }
        public string Gestures { get; }

        private AssetDirectories(string targetRoot)
        {
            TargetRoot = targetRoot;
            Root = Path.Combine(targetRoot, "assets");
            Audio = Path.Combine(Root, "audio");
            Video = Path.Combine(Root, "video");
            PreviewVideo = Path.Combine(Root, "previewVideo");
            Coaches = Path.Combine(Root, "coaches");
            Pictograms = Path.Combine(Root, "pictograms");
            Branding = Path.Combine(Root, "branding");
            Moves = Path.Combine(Root, "moves");
            Gestures = Path.Combine(Root, "gestures");
        }

        public static AssetDirectories Create(string targetRoot) => new(targetRoot);

        public string ToRelative(string absolutePath) => NormalizePath(Path.GetRelativePath(TargetRoot, absolutePath));
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}