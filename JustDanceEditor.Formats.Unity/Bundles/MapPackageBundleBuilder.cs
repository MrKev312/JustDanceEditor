using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityMoveFile(string FileName, byte[] Content);

public sealed record UnityMapPackageRequest(
    string SongName,
    UnityExportData UnityData,
    IReadOnlyList<string> PictoFiles,
    string PictoTempFolder,
    string PictoAtlasFolder,
    string? MovesFolder,
    string TemplatePath,
    string OutputFolderPath,
    bool ForCustomServer);

public static class MapPackageBundleBuilder
{
    public static Task GenerateAsync(UnityMapPackageRequest request) =>
        Task.Run(() => Generate(request));

    public static void Generate(UnityMapPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request);

        UnityPictoConversionRequest pictoRequest = new(
            request.UnityData,
            request.PictoFiles ?? Array.Empty<string>(),
            request.PictoAtlasFolder);

        Logger.Log($"Converting MapPackage for {request.SongName}...");

        UnityPictoConversionResult pictoResult = UnityPictoConverter.Convert(pictoRequest);
        List<UnityMoveFile> moveFiles = LoadMoveFiles(request.MovesFolder);

        try
        {
            BundleContext internalRequest = new(
                request.SongName,
                request.UnityData,
                pictoResult.ImageDictionary,
                pictoResult.AtlasImages,
                moveFiles,
                request.TemplatePath,
                request.OutputFolderPath,
                request.ForCustomServer);

            GenerateBundle(internalRequest);
        }
        finally
        {
            foreach (Image<Rgba32> atlas in pictoResult.AtlasImages)
                atlas.Dispose();
        }
    }

    private static void GenerateBundle(BundleContext request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBundleRequest(request);

        Logger.Log($"Converting MapPackage bundle for {request.Codename}...");
        try
        {
            (AssetsManager? manager, BundleFileInstance? bunInst, AssetsFileInstance? afileInst, AssetsFile? afile, AssetBundleFile? bunFile, List<AssetFileInfo>? sortedAssetInfos, AssetFileInfo? musicTrackInfo, AssetFileInfo? mapInfo) = InitializeBundle(request);
            (AssetTypeValueField? musicTrackBase, AssetTypeValueField? mapBase, AssetFileInfo? assetBundleInfo, AssetTypeValueField? assetBundleBase) = IdentifyAndPrepareMonoBehavioursAndAssetBundle(request, manager, afileInst, sortedAssetInfos);
            AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

            AssetFileInfo spriteTemplate = ClearExistingMapAssets(manager, afileInst, afile, sortedAssetInfos, assetBundleArray);
            AssetFileInfo spriteAtlasInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.SpriteAtlas);
            AssetTypeValueField spriteAtlasBase = PrepareSpriteAtlas(request, manager, afileInst, spriteAtlasInfo);

            UpdateMusicTrackData(request.UnityData, musicTrackBase);
            UpdateKaraokeData(request.UnityData, mapBase);
            AddDanceMoveAssets(request, manager, afileInst, afile, mapBase["HandDeviceMoveModels"]["list"]["Array"], assetBundleArray);

            long[] atlasIds = AddPictoAtlasTextureAssets(request, manager, afileInst, afile, assetBundleArray);
            AddPictoSpriteAssets(request, manager, afileInst, afile, spriteTemplate, spriteAtlasBase, request.PictoLookup, atlasIds, assetBundleArray);
            FinalizeSpriteAtlas(afile, spriteAtlasInfo, spriteAtlasBase, spriteTemplate);

            PopulateDanceDataClips(request, mapBase, request.PictoLookup);
            UpdateCoachCounters(request, mapBase);

            FinalizeAndSaveBundle(request, bunFile, afile, musicTrackBase, assetBundleBase,
                () => musicTrackInfo.SetNewData(musicTrackBase),
                () => mapInfo.SetNewData(mapBase),
                () => assetBundleInfo.SetNewData(assetBundleBase));

            Logger.Log($"Finished MapPackage bundle for {request.Codename}");
        }
        catch
        {
            Logger.Log($"Failed to generate MapPackage bundle for {request.Codename}", LogLevel.Error);
            throw;
        }
    }

    private static void ValidateInput(UnityMapPackageRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        ArgumentNullException.ThrowIfNull(request.UnityData);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoTempFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoAtlasFolder);
        ArgumentNullException.ThrowIfNull(request.PictoFiles);
        ArgumentNullException.ThrowIfNull(request.UnityData.Metadata);
    }

    private static void ValidateBundleRequest(BundleContext request)
    {
        if (string.IsNullOrWhiteSpace(request.Codename))
            throw new ArgumentException("Codename must be provided.", nameof(request));
        if (request.UnityData == null)
            throw new ArgumentException("Unity export data must be provided.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TemplatePath) || !File.Exists(request.TemplatePath))
            throw new FileNotFoundException("Template bundle file not found.", request.TemplatePath);
        if (string.IsNullOrWhiteSpace(request.OutputFolderPath))
            throw new ArgumentException("Output folder path must be provided.", nameof(request));
        if (request.AtlasImages == null)
            throw new ArgumentException("Atlas images collection must not be null.", nameof(request));
        if (request.PictoLookup == null)
            throw new ArgumentException("Picto lookup dictionary must not be null.", nameof(request));
        if (request.MoveFiles == null)
            throw new ArgumentException("Move files collection must not be null.", nameof(request));
    }

    private static List<UnityMoveFile> LoadMoveFiles(string? movesFolder)
    {
        List<UnityMoveFile> moves = [];
        if (string.IsNullOrWhiteSpace(movesFolder) || !Directory.Exists(movesFolder))
        {
            Logger.Log("Moves folder not found, skipping dance move asset addition.", LogLevel.Info);
            return moves;
        }

        foreach (string path in Directory.GetFiles(movesFolder, "*.msm"))
        {
            byte[] content = File.ReadAllBytes(path);
            moves.Add(new UnityMoveFile(Path.GetFileName(path), content));
        }

        return moves;
    }

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, AssetBundleFile BunFile, List<AssetFileInfo> SortedAssetInfos, AssetFileInfo MusicTrackInfo, AssetFileInfo MapInfo)
        InitializeBundle(BundleContext request)
    {
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(request.TemplatePath, true);
        AssetBundleFile bunFile = bunInst.file;
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();
        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        AssetFileInfo[] musicTrackInfos = [.. sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour)];
        AssetFileInfo musicTrackInfo, mapInfo;

        AssetTypeValueField firstTrackField = manager.GetBaseField(afileInst, musicTrackInfos[0]);
        bool isFirstTrackMusicTrack = firstTrackField["m_Name"].AsString == string.Empty;
        musicTrackInfo = isFirstTrackMusicTrack ? musicTrackInfos[0] : musicTrackInfos[1];
        mapInfo = isFirstTrackMusicTrack ? musicTrackInfos[1] : musicTrackInfos[0];

        AssetTypeValueField mapBaseTemp = manager.GetBaseField(afileInst, mapInfo);
        if (mapBaseTemp["m_Name"].AsString == string.Empty)
            throw new InvalidOperationException("MapBehaviour name is empty, template might be corrupted or incorrect.");

        return (manager, bunInst, afileInst, afile, bunFile, sortedAssetInfos, musicTrackInfo, mapInfo);
    }

    private static (AssetTypeValueField musicTrackBase, AssetTypeValueField mapBase, AssetFileInfo assetBundleInfo, AssetTypeValueField assetBundleBase)
        IdentifyAndPrepareMonoBehavioursAndAssetBundle(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, List<AssetFileInfo> sortedAssetInfos)
    {
        AssetFileInfo[] monoBehaviourInfos = [.. sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour)];
        AssetTypeValueField track1 = manager.GetBaseField(afileInst, monoBehaviourInfos[0]);
        AssetFileInfo musicTrackInfo, mapInfo;

        if (track1["m_Name"].AsString == string.Empty)
        {
            musicTrackInfo = monoBehaviourInfos[0];
            mapInfo = monoBehaviourInfos[1];
        }
        else
        {
            musicTrackInfo = monoBehaviourInfos[1];
            mapInfo = monoBehaviourInfos[0];
        }

        AssetTypeValueField musicTrackBase = manager.GetBaseField(afileInst, musicTrackInfo);
        AssetTypeValueField mapBase = manager.GetBaseField(afileInst, mapInfo);

        if (mapBase["m_Name"].AsString == string.Empty)
            throw new InvalidOperationException("Identified MapBehaviour has an empty name. Check template integrity.");

        mapBase["m_Name"].AsString = request.Codename;
        mapBase["MapName"].AsString = request.Codename;
        mapBase["SongDesc"]["MapName"].AsString = request.Codename;
        mapBase["SongDesc"]["NumCoach"].AsInt = request.UnityData.Metadata.CoachCount;
        mapBase["KaraokeData"]["MapName"].AsString = request.Codename;
        mapBase["DanceData"]["MapName"].AsString = request.Codename;

        AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundleInfo);
        assetBundleBase["m_Name"].AsString = $"{request.Codename}_MapPackage";
        assetBundleBase["m_AssetBundleName"].AsString = $"{request.Codename}_MapPackage";

        return (musicTrackBase, mapBase, assetBundleInfo, assetBundleBase);
    }

    private static AssetFileInfo ClearExistingMapAssets(AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, List<AssetFileInfo> sortedAssetInfos, AssetTypeValueField assetBundleArray)
    {
        List<AssetFileInfo> assetsToRemove = [];
        List<AssetTypeValueField> preloadEntriesToRemove = [];

        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.TextAsset))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);
            if (assetBase["m_Name"].AsString.EndsWith(".msm", StringComparison.Ordinal))
            {
                assetsToRemove.Add(assetInfo);
                preloadEntriesToRemove.AddRange(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId));
            }
        }

        assetsToRemove.AddRange(sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D));
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D))
            preloadEntriesToRemove.AddRange(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId));

        AssetFileInfo? spriteTemplate = null;
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite))
        {
            preloadEntriesToRemove.AddRange(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId));
            if (spriteTemplate == null)
            {
                spriteTemplate = assetInfo;
            }
            else
            {
                assetsToRemove.Add(assetInfo);
            }
        }

        foreach (AssetTypeValueField entry in preloadEntriesToRemove.Distinct())
            assetBundleArray.Children.Remove(entry);
        foreach (AssetFileInfo asset in assetsToRemove.Distinct())
            afile.AssetInfos.Remove(asset);

        if (spriteTemplate == null)
            throw new InvalidOperationException("Sprite template for pictos not found in template bundle.");

        return spriteTemplate;
    }

    private static AssetTypeValueField PrepareSpriteAtlas(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo spriteAtlasInfo)
    {
        AssetTypeValueField spriteAtlasBase = manager.GetBaseField(afileInst, spriteAtlasInfo);
        spriteAtlasBase["m_Name"].AsString = request.Codename;
        spriteAtlasBase["m_Tag"].AsString = request.Codename;
        spriteAtlasBase["m_PackedSprites"]["Array"].Children.Clear();
        spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"].Children.Clear();
        spriteAtlasBase["m_RenderDataMap"]["Array"].Children.Clear();
        return spriteAtlasBase;
    }

    private static void UpdateMusicTrackData(UnityExportData unityData, AssetTypeValueField musicTrackBase)
    {
        TimelineStructureDocument trackStructure = unityData.Structure ?? new TimelineStructureDocument();
        AssetTypeValueField structureField = musicTrackBase["m_structure"]["MusicTrackStructure"];

        structureField["startBeat"].AsInt = trackStructure.StartBeat;
        structureField["endBeat"].AsInt = trackStructure.EndBeat;
        structureField["videoStartTime"].AsDouble = trackStructure.VideoStartOffset;
        structureField["previewEntry"].AsDouble = trackStructure.PreviewEntryBeat;
        structureField["previewLoopStart"].AsDouble = trackStructure.PreviewLoopStartBeat;
        structureField["previewLoopEnd"].AsDouble = trackStructure.PreviewLoopEndBeat;
        if (!structureField["previewDuration"].IsDummy)
            structureField["previewDuration"].AsDouble = trackStructure.PrevewDuration;

        AssetTypeValueField signaturesArray = structureField["signatures"]["Array"];
        signaturesArray.Children.Clear();
        foreach (SignatureSegment signature in trackStructure.Signatures ?? [])
        {
            AssetTypeValueField newSig = ValueBuilder.DefaultValueFieldFromArrayTemplate(signaturesArray);
            newSig["MusicSignature"]["beats"].AsInt = signature.Beats;
            newSig["MusicSignature"]["marker"].AsDouble = signature.Marker;
            newSig["MusicSignature"]["comment"].AsString = signature.Comment;
            signaturesArray.Children.Add(newSig);
        }

        AssetTypeValueField markersArray = structureField["markers"]["Array"];
        markersArray.Children.Clear();
        foreach (int marker in trackStructure.Markers ?? [])
        {
            AssetTypeValueField newMarker = ValueBuilder.DefaultValueFieldFromArrayTemplate(markersArray);
            newMarker["VAL"].AsLong = (int)Math.Round(marker * 48d);
            markersArray.Children.Add(newMarker);
        }

        AssetTypeValueField sectionsArray = structureField["sections"]["Array"];
        sectionsArray.Children.Clear();
        foreach (SectionSegment section in trackStructure.Sections ?? [])
        {
            AssetTypeValueField newSection = ValueBuilder.DefaultValueFieldFromArrayTemplate(sectionsArray);
            newSection["MusicSection"]["sectionType"].AsInt = section.SectionType;
            newSection["MusicSection"]["marker"].AsDouble = section.StartBeat;
            newSection["MusicSection"]["comment"].AsString = section.Comment ?? string.Empty;
            sectionsArray.Children.Add(newSection);
        }
    }

    private static void UpdateKaraokeData(UnityExportData unityData, AssetTypeValueField mapBase)
    {
        AssetTypeValueField karaokeArray = mapBase["KaraokeData"]["Clips"]["Array"];
        karaokeArray.Children.Clear();
        foreach (KaraokeClip clip in unityData.KaraokeClips.OrderBy(c => c.StartTime))
        {
            AssetTypeValueField newClipContainer = ValueBuilder.DefaultValueFieldFromArrayTemplate(karaokeArray);
            AssetTypeValueField karaokeClipField = newClipContainer["KaraokeClip"];

            karaokeClipField["StartTime"].AsInt = clip.StartTime;
            karaokeClipField["Duration"].AsInt = clip.Duration;
            karaokeClipField["Lyrics"].AsString = clip.Lyrics ?? string.Empty;
            karaokeClipField["IsActive"].AsUInt = 1;
            karaokeClipField["TrackId"].AsLong = clip.Id;
            karaokeClipField["Pitch"].AsFloat = clip.Pitch;
            karaokeClipField["IsEndOfLine"].AsUInt = clip.IsEndOfLine ? 1u : 0u;
            karaokeClipField["ContentType"].AsInt = clip.ContentType;
            karaokeClipField["Id"].AsLong = clip.Id;
            karaokeClipField["SemitoneTolerance"].AsFloat = (float)((clip.Tolerances?.SemitoneTolerance) ?? 0);
            karaokeClipField["StartTimeTolerance"].AsInt = clip.Tolerances?.StartTimeTolerance ?? 0;
            karaokeClipField["EndTimeTolerance"].AsInt = clip.Tolerances?.EndTimeTolerance ?? 0;

            karaokeArray.Children.Add(newClipContainer);
        }
    }

    private static void AddDanceMoveAssets(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetTypeValueField movesModelArray, AssetTypeValueField assetBundleArray)
    {
        if (request.MoveFiles.Count == 0)
        {
            Logger.Log("No move files found, skipping dance move asset addition.", LogLevel.Info);
            return;
        }

        foreach (UnityMoveFile move in request.MoveFiles)
        {
            string fileName = move.FileName;
            byte[] fileContent = move.Content;
            long newAssetId = afile.GetRandomId();

            AssetTypeValueField newTextAssetBase = manager.CreateValueBaseField(afileInst, (int)AssetClassID.TextAsset);
            newTextAssetBase["m_Name"].AsString = fileName;
            newTextAssetBase["m_Script"].AsByteArray = fileContent;

            AssetFileInfo newInfo = AssetFileInfo.Create(afile, newAssetId, (int)AssetClassID.TextAsset, null);
            newInfo.SetNewData(newTextAssetBase);
            afile.Metadata.AddAssetInfo(newInfo);

            AssetTypeValueField newMoveEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(movesModelArray);
            newMoveEntry["Key"].AsString = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            newMoveEntry["Value"]["m_FileID"].AsInt = 0;
            newMoveEntry["Value"]["m_PathID"].AsLong = newAssetId;
            movesModelArray.Children.Add(newMoveEntry);

            AssetTypeValueField newPreloadEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newPreloadEntry["m_PathID"].AsLong = newAssetId;
            assetBundleArray.Children.Add(newPreloadEntry);
        }
    }

    private static long[] AddPictoAtlasTextureAssets(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetTypeValueField assetBundleArray)
    {
        if (request.AtlasImages.Count == 0)
            return [];

        byte[][] encodedAtlasBytes = new byte[request.AtlasImages.Count][];
        Parallel.For(0, request.AtlasImages.Count, i =>
        {
            using Image<Rgba32> clone = request.AtlasImages[i].CloneAs<Rgba32>();
            int mips = 1;
            encodedAtlasBytes[i] = TextureImportExport.Import(clone, TextureFormat.DXT5Crunched, out _, out _, ref mips)
                ?? throw new InvalidOperationException($"Failed to encode atlas image at index {i}.");
        });

        long[] atlasIds = new long[request.AtlasImages.Count];
        for (int i = 0; i < request.AtlasImages.Count; i++)
        {
            long newAssetId = afile.GetRandomId();
            AssetTypeValueField texBaseField = manager.CreateValueBaseField(afileInst, (int)AssetClassID.Texture2D);
            Image<Rgba32> atlasImage = request.AtlasImages[i];

            texBaseField["m_Name"].AsString = $"sactx-{i}-{atlasImage.Width}x{atlasImage.Height}-Crunch-{request.Codename}-pictoatlas";
            texBaseField["m_MipCount"].AsInt = 1;
            texBaseField["m_StreamData"]["offset"].AsULong = 0;
            texBaseField["m_StreamData"]["size"].AsUInt = 0;
            texBaseField["m_StreamData"]["path"].AsString = string.Empty;
            texBaseField["m_ForcedFallbackFormat"].AsInt = (int)TextureFormat.RGBA32;
            texBaseField["m_TextureFormat"].AsInt = (int)TextureFormat.DXT5Crunched;
            texBaseField["m_CompleteImageSize"].AsUInt = (uint)encodedAtlasBytes[i].Length;
            texBaseField["m_ImageCount"].AsInt = 1;
            texBaseField["m_TextureDimension"].AsInt = 2;
            texBaseField["m_TextureSettings"]["m_FilterMode"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_Aniso"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapU"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapV"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapW"].AsInt = 1;
            texBaseField["m_ColorSpace"].AsInt = 1;
            texBaseField["m_Width"].AsInt = atlasImage.Width;
            texBaseField["m_Height"].AsInt = atlasImage.Height;
            texBaseField["image data"].AsByteArray = encodedAtlasBytes[i];

            AssetFileInfo newInfo = AssetFileInfo.Create(afile, newAssetId, (int)AssetClassID.Texture2D, null);
            newInfo.SetNewData(texBaseField);
            afile.Metadata.AddAssetInfo(newInfo);
            atlasIds[i] = newAssetId;

            AssetTypeValueField newPreloadEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newPreloadEntry["m_PathID"].AsLong = newAssetId;
            assetBundleArray.Children.Add(newPreloadEntry);
        }

        return atlasIds;
    }

    private static void AddPictoSpriteAssets(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetFileInfo spriteTemplate,
        AssetTypeValueField spriteAtlasBase, IReadOnlyDictionary<string, (int AtlasIndex, (int Width, int Height) Size)> imageDict, long[] atlasIds, AssetTypeValueField assetBundleArray)
    {
        List<string> sortedPictoNames = [.. imageDict.Keys];
        sortedPictoNames.Sort(StringComparer.InvariantCulture);

        int coachCount = Math.Max(1, request.UnityData.Metadata.CoachCount);

        for (int i = 0; i < sortedPictoNames.Count; i++)
        {
            string pictoName = sortedPictoNames[i];
            (int atlasPageIndex, (int Width, int Height) size) = imageDict[pictoName];

            long spriteId = afile.GetRandomId();
            AssetTypeValueField spriteBaseField = manager.GetBaseField(afileInst, spriteTemplate);

            float pixelsToUnits = coachCount == 1 ? 100f : 69.140625f;
            float wMagic = coachCount == 1 ? 256f : 177f;

            spriteBaseField["m_Name"].AsString = pictoName;
            spriteBaseField["m_Rect"]["width"].AsFloat = size.Width;
            spriteBaseField["m_Rect"]["height"].AsFloat = size.Height;
            spriteBaseField["m_PixelsToUnits"].AsFloat = pixelsToUnits;
            spriteBaseField["m_AtlasTags"]["Array"].Children[0].AsString = request.Codename;

            spriteBaseField["m_RD"]["textureRect"]["x"].AsFloat = 0;
            spriteBaseField["m_RD"]["textureRect"]["y"].AsFloat = 0;
            spriteBaseField["m_RD"]["textureRect"]["width"].AsFloat = size.Width;
            spriteBaseField["m_RD"]["textureRect"]["height"].AsFloat = size.Height;
            spriteBaseField["m_RD"]["textureRectOffset"]["x"].AsFloat = 0;
            spriteBaseField["m_RD"]["textureRectOffset"]["y"].AsFloat = 0;
            spriteBaseField["m_RD"]["settingsRaw"].AsUInt = 0;
            spriteBaseField["m_RD"]["uvTransform"]["x"].AsFloat = pixelsToUnits;
            spriteBaseField["m_RD"]["uvTransform"]["y"].AsFloat = 256;
            spriteBaseField["m_RD"]["uvTransform"]["z"].AsFloat = pixelsToUnits;
            spriteBaseField["m_RD"]["uvTransform"]["w"].AsFloat = wMagic;

            uint[] guidUnity = Guid.NewGuid().ToUnity();
            spriteBaseField["m_RenderDataKey"]["first"]["data[0]"].AsUInt = guidUnity[0];
            spriteBaseField["m_RenderDataKey"]["first"]["data[1]"].AsUInt = guidUnity[1];
            spriteBaseField["m_RenderDataKey"]["first"]["data[2]"].AsUInt = guidUnity[2];
            spriteBaseField["m_RenderDataKey"]["first"]["data[3]"].AsUInt = guidUnity[3];

            spriteBaseField["m_RD"]["m_SubMeshes"]["Array"][0]["indexCount"].AsUInt = 6;
            spriteBaseField["m_RD"]["m_SubMeshes"]["Array"][0]["vertexCount"].AsUInt = 4;
            spriteBaseField["m_RD"]["m_IndexBuffer"]["Array"].AsByteArray = [0, 0, 1, 0, 2, 0, 2, 0, 1, 0, 3, 0];
            spriteBaseField["m_RD"]["m_VertexData"]["m_VertexCount"].AsUInt = 4;
            byte[] vertexDataBytes =
                [10, 215, 35, 192, 10, 215, 35, 64, 0, 0, 0, 0, 10, 215, 35, 64, 10, 215, 35, 64,
             0, 0, 0, 0, 10, 215, 35, 192, 10, 215, 35, 192, 0, 0, 0, 0, 10, 215, 35, 64, 10,
             215, 35, 192, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
             0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            byte[] vertexMagics = coachCount == 1 ? [10, 215, 35] : [97, 247, 108];
            for (int j = 0; j <= 36; j += 12)
            {
                vertexDataBytes[j] = vertexMagics[0];
                vertexDataBytes[j + 1] = vertexMagics[1];
                vertexDataBytes[j + 2] = vertexMagics[2];
            }

            spriteBaseField["m_RD"]["m_VertexData"]["m_DataSize"].AsByteArray = vertexDataBytes;

            AssetFileInfo newSpriteInfo = AssetFileInfo.Create(afile, spriteId, (int)AssetClassID.Sprite, null);
            newSpriteInfo.SetNewData(spriteBaseField);
            afile.Metadata.AddAssetInfo(newSpriteInfo);

            AssetTypeValueField newPreloadEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newPreloadEntry["m_PathID"].AsLong = spriteId;
            assetBundleArray.Children.Add(newPreloadEntry);

            AssetTypeValueField packedSpriteEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_PackedSprites"]["Array"]);
            packedSpriteEntry["m_PathID"].AsLong = spriteId;
            spriteAtlasBase["m_PackedSprites"]["Array"].Children.Add(packedSpriteEntry);

            AssetTypeValueField packedSpriteNameEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"]);
            packedSpriteNameEntry.AsString = pictoName;
            spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"].Children.Add(packedSpriteNameEntry);

            AssetTypeValueField renderDataEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_RenderDataMap"]["Array"]);
            renderDataEntry["first"]["first"]["data[0]"].AsUInt = guidUnity[0];
            renderDataEntry["first"]["first"]["data[1]"].AsUInt = guidUnity[1];
            renderDataEntry["first"]["first"]["data[2]"].AsUInt = guidUnity[2];
            renderDataEntry["first"]["first"]["data[3]"].AsUInt = guidUnity[3];
            renderDataEntry["first"]["second"].AsLong = 21300000;

            int indexInAtlasGrid = i % 16;
            int xOffset = indexInAtlasGrid % 4 * 512;
            int yOffset = indexInAtlasGrid / 4 * 512;

            if (atlasPageIndex >= atlasIds.Length || atlasIds[atlasPageIndex] == 0)
            {
                Logger.Log($"Atlas texture for picto '{pictoName}' (atlas index {atlasPageIndex}) is invalid. Skipping RenderDataMap entry.", LogLevel.Error);
                continue;
            }

            renderDataEntry["second"]["texture"]["m_PathID"].AsLong = atlasIds[atlasPageIndex];
            renderDataEntry["second"]["textureRect"]["x"].AsFloat = xOffset;
            renderDataEntry["second"]["textureRect"]["y"].AsFloat = yOffset;
            renderDataEntry["second"]["textureRect"]["width"].AsFloat = size.Width;
            renderDataEntry["second"]["textureRect"]["height"].AsFloat = size.Height;
            renderDataEntry["second"]["atlasRectOffset"]["x"].AsFloat = xOffset;
            renderDataEntry["second"]["atlasRectOffset"]["y"].AsFloat = yOffset;
            renderDataEntry["second"]["uvTransform"]["x"].AsFloat = pixelsToUnits;
            renderDataEntry["second"]["uvTransform"]["y"].AsFloat = 256 + xOffset;
            renderDataEntry["second"]["uvTransform"]["z"].AsFloat = pixelsToUnits;
            renderDataEntry["second"]["uvTransform"]["w"].AsFloat = wMagic + yOffset;
            renderDataEntry["second"]["downscaleMultiplier"].AsFloat = 1;
            renderDataEntry["second"]["settingsRaw"].AsUInt = 3;

            spriteAtlasBase["m_RenderDataMap"]["Array"].Children.Add(renderDataEntry);
        }
    }

    private static void FinalizeSpriteAtlas(AssetsFile afile, AssetFileInfo spriteAtlasInfo, AssetTypeValueField spriteAtlasBase, AssetFileInfo spriteTemplate)
    {
        spriteAtlasInfo.SetNewData(spriteAtlasBase);
        afile.AssetInfos.Remove(spriteTemplate);
    }

    private static void PopulateDanceDataClips(BundleContext request, AssetTypeValueField mapBase, IReadOnlyDictionary<string, (int index, (int Width, int Height) size)> imageDict)
    {
        AssetTypeValueField motionClipsArray = mapBase["DanceData"]["MotionClips"]["Array"];
        AssetTypeValueField goldEffectClipsArray = mapBase["DanceData"]["GoldEffectClips"]["Array"];
        AssetTypeValueField hideHudClipsArray = mapBase["DanceData"]["HideHudClips"]["Array"];
        AssetTypeValueField pictoClipsArray = mapBase["DanceData"]["PictoClips"]["Array"];

        motionClipsArray.Children.Clear();
        goldEffectClipsArray.Children.Clear();
        hideHudClipsArray.Children.Clear();
        pictoClipsArray.Children.Clear();

        int coachCount = Math.Max(1, request.UnityData.Metadata.CoachCount);

        foreach (GoldEffectTimelineClip gold in request.UnityData.GoldEffectClips.OrderBy(c => c.StartTime))
        {
            AssetTypeValueField newGold = ValueBuilder.DefaultValueFieldFromArrayTemplate(goldEffectClipsArray);
            newGold["StartTime"].AsInt = gold.StartTime;
            newGold["Duration"].AsInt = gold.Duration;
            newGold["GoldEffectType"].AsInt = gold.EffectType;
            newGold["Id"].AsLong = gold.Id;
            newGold["TrackId"].AsLong = gold.TrackId;
            newGold["IsActive"].AsUInt = gold.IsActive ? 1u : 0u;
            goldEffectClipsArray.Children.Add(newGold);
        }

        foreach (PictogramEntry picto in request.UnityData.PictogramClips.OrderBy(c => c.StartTime))
        {
            AssetTypeValueField newPicto = ValueBuilder.DefaultValueFieldFromArrayTemplate(pictoClipsArray);
            string pictoName = string.IsNullOrWhiteSpace(picto.PictogramId) ? $"picto_{picto.Id}" : picto.PictogramId;
            if (!imageDict.ContainsKey(pictoName))
            {
                string? foundKey = imageDict.Keys.FirstOrDefault(k => k.Equals(pictoName, StringComparison.InvariantCultureIgnoreCase));
                if (foundKey != null)
                    pictoName = foundKey;
                else
                    Logger.Log($"Pictogram '{pictoName}' for clip not found in image dictionary. Clip might not display correctly.", LogLevel.Warning);
            }

            newPicto["StartTime"].AsInt = picto.StartTime;
            newPicto["Duration"].AsInt = picto.Duration == 0 ? 16 : picto.Duration;
            newPicto["Id"].AsLong = picto.Id;
            newPicto["TrackId"].AsLong = picto.Id;
            newPicto["IsActive"].AsUInt = 1;
            newPicto["PictoPath"].AsString = pictoName;
            newPicto["CoachCount"].AsUInt = (uint)picto.CoachCount;
            pictoClipsArray.Children.Add(newPicto);
        }

        foreach ((CoachTimelineClip clip, int coachId, long trackId, int moveType, int duration) in request.UnityData.MotionClips.OrderBy(m => m.Clip.StartTime))
        {
            if (coachId < 0 || coachId >= coachCount)
                continue;

            string moveName = string.IsNullOrWhiteSpace(clip.MoveId)
                ? $"move_{coachId}"
                : clip.MoveId.ToLowerInvariant();

            AssetTypeValueField newMotion = ValueBuilder.DefaultValueFieldFromArrayTemplate(motionClipsArray);
            newMotion["StartTime"].AsInt = clip.StartTime;
            newMotion["Duration"].AsInt = duration;
            newMotion["Id"].AsLong = clip.Id;
            newMotion["TrackId"].AsLong = trackId;
            newMotion["IsActive"].AsUInt = 1;
            newMotion["MoveName"].AsString = moveName;
            newMotion["GoldMove"].AsUInt = clip.IsGoldMove ? 1u : 0u;
            newMotion["CoachId"].AsInt = coachId;
            newMotion["MoveType"].AsInt = moveType;
            newMotion["Color"].AsString = string.Empty;
            motionClipsArray.Children.Add(newMotion);
        }

        foreach (HideUserInterfaceTimelineClip hideHud in request.UnityData.HideHudClips.OrderBy(c => c.StartTime))
        {
            AssetTypeValueField newHideHud = ValueBuilder.DefaultValueFieldFromArrayTemplate(hideHudClipsArray);
            newHideHud["StartTime"].AsInt = hideHud.StartTime;
            newHideHud["Duration"].AsInt = hideHud.Duration;
            newHideHud["IsActive"].AsUInt = hideHud.IsActive ? 1u : 0u;
            hideHudClipsArray.Children.Add(newHideHud);
        }
    }

    private static void UpdateCoachCounters(BundleContext request, AssetTypeValueField mapBase)
    {
        AssetTypeValueField handCoachCounters = mapBase["HandOnlyCoachDatas"]["Array"];
        AssetTypeValueField bodyCoachCounters = mapBase["FullBodyCoachDatas"]["Array"];

        if (bodyCoachCounters == null || bodyCoachCounters.IsDummy)
            bodyCoachCounters = ValueBuilder.DefaultValueFieldFromArrayTemplate(handCoachCounters);

        handCoachCounters.Children.Clear();
        bodyCoachCounters.Children.Clear();

        int coachCount = Math.Max(1, request.UnityData.Metadata.CoachCount);
        for (int i = 0; i < coachCount; i++)
        {
            AssetTypeValueField newHandCounter = ValueBuilder.DefaultValueFieldFromArrayTemplate(handCoachCounters);
            newHandCounter["GoldMovesCount"].AsUInt = 0;
            newHandCounter["StandardMovesCount"].AsUInt = 0;
            handCoachCounters.Children.Add(newHandCounter);

            AssetTypeValueField newBodyCounter = ValueBuilder.DefaultValueFieldFromArrayTemplate(bodyCoachCounters);
            newBodyCounter["GoldMovesCount"].AsUInt = 0;
            newBodyCounter["StandardMovesCount"].AsUInt = 0;
            bodyCoachCounters.Children.Add(newBodyCounter);
        }

        foreach (AssetTypeValueField motionClipField in mapBase["DanceData"]["MotionClips"]["Array"].Children)
        {
            int coachId = motionClipField["CoachId"].AsInt;
            int moveType = motionClipField["MoveType"].AsInt;
            bool isGoldMove = motionClipField["GoldMove"].AsUInt == 1;

            if (coachId >= 0 && coachId < handCoachCounters.Children.Count)
            {
                if (moveType == 0)
                {
                    if (isGoldMove)
                        handCoachCounters.Children[coachId]["GoldMovesCount"].AsUInt++;
                    else
                        handCoachCounters.Children[coachId]["StandardMovesCount"].AsUInt++;
                }
                else if (moveType == 1)
                {
                    if (isGoldMove)
                        bodyCoachCounters.Children[coachId]["GoldMovesCount"].AsUInt++;
                    else
                        bodyCoachCounters.Children[coachId]["StandardMovesCount"].AsUInt++;
                }
            }
        }
    }

    private static void FinalizeAndSaveBundle(BundleContext request, AssetBundleFile bun, AssetsFile afile,
        AssetTypeValueField musicTrackBase, AssetTypeValueField assetBundleBase,
        Action setMusicTrackData, Action setMapData, Action setAssetBundleData)
    {
        setMusicTrackData();
        setMapData();

        AssetTypeValueField preloadTableArray = assetBundleBase["m_PreloadTable"]["Array"];
        AssetTypeValueField containerArray = assetBundleBase["m_Container"]["Array"];
        if (containerArray != null && !containerArray.IsDummy)
        {
            for (int i = 0; i < Math.Min(2, containerArray.Children.Count); i++)
                containerArray[i]["second"]["preloadSize"].AsInt = preloadTableArray.Children.Count;
        }

        setAssetBundleData();
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        bun.SaveAndCompress(request.OutputFolderPath, request.ForCustomServer);
    }

    private sealed record BundleContext(
        string Codename,
        UnityExportData UnityData,
        IReadOnlyDictionary<string, (int AtlasIndex, (int Width, int Height) Size)> PictoLookup,
        IReadOnlyList<Image<Rgba32>> AtlasImages,
        IReadOnlyList<UnityMoveFile> MoveFiles,
        string TemplatePath,
        string OutputFolderPath,
        bool ForCustomServer);
}