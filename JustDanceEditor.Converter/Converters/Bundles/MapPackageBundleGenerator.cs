using AssetsTools.NET.Extra;
using AssetsTools.NET;

using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.UbiArt.Tapes;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using JustDanceEditor.Converter.Core;

namespace JustDanceEditor.Converter.Converters.Bundles;
public static class MapPackageBundleGenerator
{
    public async static Task GenerateMapPackageAsync(ConversionContext context) =>
        await Task.Run(() => GenerateMapPackage(context));

    public static void GenerateMapPackage(ConversionContext context)
    {
        try
        {
            GenerateMapPackageInternally(context);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate map package: {e.Message}", LogLevel.Error);
        }

        Logger.Log("Finished generating map package");
    }

    private static void GenerateMapPackageInternally(ConversionContext context)
    {
        // Start asynchronous conversion of pictograms
        Task<(Dictionary<string, (int index, (int Width, int Height))>, List<Image<Rgba32>>)> pictoTask =
            Task.Run(() => PictoConverter.ConvertPictos(context));

        Logger.Log("Converting MapPackage...");
        // Initialize AssetsManager and load bundle data
        var (Manager, BunInst, AFileInst, AFile, BunFile, SortedAssetInfos, MusicTrackInfo, MapInfo) = InitializeBundle(context);
        var manager = Manager;
        var afileInst = AFileInst;
        var afile = AFile;
        var bun = BunFile;
        var sortedAssetInfos = SortedAssetInfos;

        // Identify MusicTrack and MapBehaviour MonoBehaviours and the main AssetBundle asset
        var (musicTrackBase, mapBase, assetBundleInfo, assetBundleBase) = IdentifyAndPrepareMonoBehavioursAndAssetBundle(context, manager, afileInst, sortedAssetInfos);
        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        // Remove old dance moves (TextAssets) and pictos (Texture2D, Sprite) from the bundle, retaining a sprite template
        var spriteTemplate = ClearExistingMapAssets(manager, afileInst, afile, sortedAssetInfos, assetBundleArray);

        // Prepare the SpriteAtlas for new pictos
        var spriteAtlasInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.SpriteAtlas);
        var spriteAtlasBase = PrepareSpriteAtlas(context, manager, afileInst, spriteAtlasInfo);

        // Update the MusicTrack MonoBehaviour with data from the song
        UpdateMusicTrackData(context, musicTrackBase);

        // Update the MapBehaviour MonoBehaviour with karaoke clip data
        UpdateKaraokeData(context, mapBase);

        // Add new dance move assets (msm files) to the bundle
        AddDanceMoveAssets(context, manager, afileInst, afile, mapBase["HandDeviceMoveModels"]["list"]["Array"], assetBundleArray);

        // Wait for picto conversion, then process and add picto atlas textures to the bundle
        var (imageDict, atlasPics) = pictoTask.Result;
        long[] atlasIDs = AddPictoAtlasTextureAssets(context, manager, afileInst, afile, atlasPics, assetBundleArray);

        // Process and add individual picto sprites to the bundle and update the SpriteAtlas
        AddPictoSpriteAssets(context, manager, afileInst, afile, spriteTemplate, spriteAtlasBase, imageDict, atlasIDs, assetBundleArray);

        // Finalize changes to the SpriteAtlas and remove the template sprite
        FinalizeSpriteAtlas(afile, spriteAtlasInfo, spriteAtlasBase, spriteTemplate);

        // Populate dance data clips (Motion, GoldEffect, HideHud, Picto) in the MapBehaviour
        PopulateDanceDataClips(context, mapBase, imageDict);

        // Update coach move counters in the MapBehaviour
        UpdateCoachCounters(context, mapBase);

        // Apply changes to MonoBehaviours, update AssetBundle container, and save the bundle
        FinalizeAndSaveBundle(context, bun, afile, musicTrackBase, mapBase, assetBundleBase,
            () => MusicTrackInfo.SetNewData(musicTrackBase),
            () => MapInfo.SetNewData(mapBase),
            () => assetBundleInfo.SetNewData(assetBundleBase)
        );
    }

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, AssetBundleFile BunFile, List<AssetFileInfo> SortedAssetInfos, AssetFileInfo MusicTrackInfo, AssetFileInfo MapInfo)
        InitializeBundle(ConversionContext context)
    {
        string mapPackagePath = context.FileSystem.TemplateFiles.MapPackage;
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(mapPackagePath, true);
        AssetBundleFile bunFile = bunInst.file;
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();
        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        AssetFileInfo[] musicTrackInfos = [.. sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour)];
        AssetFileInfo musicTrackInfo, mapInfo;

        AssetTypeValueField firstTrackField = manager.GetBaseField(afileInst, musicTrackInfos[0]);
        bool isFirstTrackMusicTrack = firstTrackField["m_Name"].AsString == ""; // MusicTrack often has an empty name
        musicTrackInfo = isFirstTrackMusicTrack ? musicTrackInfos[0] : musicTrackInfos[1];
        mapInfo = isFirstTrackMusicTrack ? musicTrackInfos[1] : musicTrackInfos[0];

        AssetTypeValueField mapBaseTemp = manager.GetBaseField(afileInst, mapInfo);
        if (mapBaseTemp["m_Name"].AsString == "")
            throw new Exception("MapBehaviour name is empty, template might be corrupted or incorrect. Cannot identify MapBehaviour.");

        return (manager, bunInst, afileInst, afile, bunFile, sortedAssetInfos, musicTrackInfo, mapInfo);
    }

    private static (AssetTypeValueField musicTrackBase, AssetTypeValueField mapBase, AssetFileInfo assetBundleInfo, AssetTypeValueField assetBundleBase)
        IdentifyAndPrepareMonoBehavioursAndAssetBundle(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, List<AssetFileInfo> sortedAssetInfos)
    {
        AssetFileInfo[] monoBehaviourInfos = [.. sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour)];
        AssetTypeValueField track1 = manager.GetBaseField(afileInst, monoBehaviourInfos[0]);
        AssetFileInfo musicTrackInfo, mapInfo;

        // MusicTrack typically has an empty m_Name, while MapBehaviour has the map's name.
        if (track1["m_Name"].AsString == "")
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

        // Verify that the identified MapBehaviour indeed has a name.
        if (mapBase["m_Name"].AsString == "")
            throw new Exception("Critical error: Identified MapBehaviour has an empty name. Check template integrity.");

        // Set map-specific names and properties in the MapBehaviour.
        mapBase["m_Name"].AsString = context.SongData.Name;
        mapBase["MapName"].AsString = context.SongData.Name;
        mapBase["SongDesc"]["MapName"].AsString = context.SongData.Name;
        mapBase["SongDesc"]["NumCoach"].AsInt = (int)context.SongData.CoachCount;
        mapBase["KaraokeData"]["MapName"].AsString = context.SongData.Name;
        mapBase["DanceData"]["MapName"].AsString = context.SongData.Name;

        AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundleInfo);
        assetBundleBase["m_Name"].AsString = $"{context.SongData.Name}_MapPackage";
        assetBundleBase["m_AssetBundleName"].AsString = $"{context.SongData.Name}_MapPackage";

        return (musicTrackBase, mapBase, assetBundleInfo, assetBundleBase);
    }

    private static AssetFileInfo ClearExistingMapAssets(AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, List<AssetFileInfo> sortedAssetInfos, AssetTypeValueField assetBundleArray)
    {
        List<AssetFileInfo> assetsToRemove = [];
        List<AssetTypeValueField> preloadEntriesToRemove = [];

        // Remove old TextAssets (specifically .msm files for dance moves)
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.TextAsset))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);
            if (assetBase["m_Name"].AsString.EndsWith(".msm"))
            {
                assetsToRemove.Add(assetInfo);
                preloadEntriesToRemove.AddRange(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId));
            }
        }

        // Remove all old Texture2D assets (pictos)
        assetsToRemove.AddRange(sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D));
        foreach (var assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D))
        {
            preloadEntriesToRemove.AddRange(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId));
        }

        // Remove old Sprite assets (pictos), but keep the first one encountered as a template for new sprites.
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

        // Perform removal from preload table and asset list
        foreach (var entry in preloadEntriesToRemove.Distinct())
            assetBundleArray.Children.Remove(entry);
        foreach (var asset in assetsToRemove.Distinct())
            afile.AssetInfos.Remove(asset);

        if (spriteTemplate == null)
            throw new Exception("Sprite template for pictos not found! Ensure the template bundle contains at least one sprite.");
        return spriteTemplate;
    }

    private static AssetTypeValueField PrepareSpriteAtlas(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo spriteAtlasInfo)
    {
        AssetTypeValueField spriteAtlasBase = manager.GetBaseField(afileInst, spriteAtlasInfo);
        spriteAtlasBase["m_Name"].AsString = context.SongData.Name; // Name the atlas after the song
        spriteAtlasBase["m_Tag"].AsString = context.SongData.Name;  // Tag the atlas similarly for lookup
        // Clear out collections that will be repopulated with new picto sprite data
        spriteAtlasBase["m_PackedSprites"]["Array"].Children.Clear();
        spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"].Children.Clear();
        spriteAtlasBase["m_RenderDataMap"]["Array"].Children.Clear();
        return spriteAtlasBase;
    }

    private static void UpdateMusicTrackData(ConversionContext context, AssetTypeValueField musicTrackBase)
    {
        Structure trackStructure = context.SongData.MusicTrack.COMPONENTS[0].trackData.structure;
        AssetTypeValueField structureField = musicTrackBase["m_structure"]["MusicTrackStructure"];

        // Basic track structure properties
        structureField["startBeat"].AsInt = trackStructure.startBeat;
        structureField["endBeat"].AsInt = trackStructure.endBeat;
        structureField["videoStartTime"].AsDouble = trackStructure.videoStartTime;
        structureField["previewEntry"].AsDouble = trackStructure.previewEntry;
        structureField["previewLoopStart"].AsDouble = trackStructure.previewLoopStart;
        structureField["previewLoopEnd"].AsDouble = 0; // Often 0 or previewLoopStart + previewDuration
        if (!structureField["previewDuration"].IsDummy) // Check if field exists
            structureField["previewDuration"].AsDouble = trackStructure.previewLoopEnd - trackStructure.previewLoopStart;

        // Populate signatures (time signatures)
        AssetTypeValueField signaturesArray = structureField["signatures"]["Array"];
        signaturesArray.Children.Clear();
        foreach (Signature signature in trackStructure.signatures)
        {
            AssetTypeValueField newSig = ValueBuilder.DefaultValueFieldFromArrayTemplate(signaturesArray);
            newSig["MusicSignature"]["beats"].AsInt = signature.beats;
            newSig["MusicSignature"]["marker"].AsDouble = signature.marker;
            newSig["MusicSignature"]["comment"].AsString = ""; // Comments usually not used
            signaturesArray.Children.Add(newSig);
        }

        // Populate markers (beat markers)
        AssetTypeValueField markersArray = structureField["markers"]["Array"];
        markersArray.Children.Clear();
        foreach (int marker in trackStructure.markers)
        {
            AssetTypeValueField newMarker = ValueBuilder.DefaultValueFieldFromArrayTemplate(markersArray);
            newMarker["VAL"].AsLong = marker; // Markers are typically integer beat numbers
            markersArray.Children.Add(newMarker);
        }

        // Populate sections (intro, verse, chorus, etc.)
        AssetTypeValueField sectionsArray = structureField["sections"]["Array"];
        sectionsArray.Children.Clear();
        foreach (Section section in trackStructure.sections)
        {
            AssetTypeValueField newSection = ValueBuilder.DefaultValueFieldFromArrayTemplate(sectionsArray);
            newSection["MusicSection"]["sectionType"].AsInt = section.sectionType;
            newSection["MusicSection"]["marker"].AsDouble = section.marker;
            newSection["MusicSection"]["comment"].AsString = ""; // Comments usually not used
            sectionsArray.Children.Add(newSection);
        }
    }

    private static void UpdateKaraokeData(ConversionContext context, AssetTypeValueField mapBase)
    {
        AssetTypeValueField karaokeArray = mapBase["KaraokeData"]["Clips"]["Array"];
        karaokeArray.Children.Clear(); // Clear existing karaoke clips
        foreach (KaraokeClip clip in context.SongData.Clips.OfType<KaraokeClip>())
        {
            AssetTypeValueField newClipContainer = ValueBuilder.DefaultValueFieldFromArrayTemplate(karaokeArray);
            AssetTypeValueField karaokeClipField = newClipContainer["KaraokeClip"];

            // Set properties for the karaoke clip
            karaokeClipField["StartTime"].AsInt = clip.StartTime;
            karaokeClipField["Duration"].AsInt = clip.Duration;
            karaokeClipField["Lyrics"].AsString = clip.Lyrics;
            karaokeClipField["IsActive"].AsUInt = (uint)clip.IsActive; // 1 for active, 0 for inactive
            karaokeClipField["TrackId"].AsLong = clip.TrackId; // Identifies the lyric line or segment
            karaokeClipField["Pitch"].AsFloat = clip.Pitch;     // Pitch of the lyric, if available
            karaokeClipField["IsEndOfLine"].AsUInt = (uint)clip.IsEndOfLine; // 1 if this clip is the end of a line
            karaokeClipField["ContentType"].AsInt = 2; // Standard content type for lyrics
            karaokeClipField["Id"].AsLong = clip.Id;           // Unique ID for the clip
            karaokeClipField["SemitoneTolerance"].AsInt = (int)clip.SemitoneTolerance; // Scoring tolerance
            karaokeClipField["StartTimeTolerance"].AsInt = clip.StartTimeTolerance;  // Scoring tolerance
            karaokeClipField["EndTimeTolerance"].AsInt = clip.EndTimeTolerance;    // Scoring tolerance

            karaokeArray.Children.Add(newClipContainer);
        }
    }

    private static void AddDanceMoveAssets(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetTypeValueField movesModelArray, AssetTypeValueField assetBundleArray)
    {
        if (!context.FileSystem.GetFolderPath(context.FileSystem.InputFolders.MovesFolder, out string? movesFolder) || !Directory.Exists(movesFolder))
        {
            Logger.Log("Moves folder not found or specified, skipping dance move asset addition.", LogLevel.Info);
            return;
        }

        string[] moveFiles = Directory.GetFiles(movesFolder, "*.msm"); // Look for .msm (move state machine) files
        foreach (string item in moveFiles)
        {
            string fileName = Path.GetFileName(item);
            byte[] fileContent = File.ReadAllBytes(item);
            long newAssetId = afile.GetRandomId(); // Generate a unique PathID for the new asset

            // Create a new TextAsset for the move file
            AssetTypeValueField newTextAssetBase = manager.CreateValueBaseField(afileInst, (int)AssetClassID.TextAsset);
            newTextAssetBase["m_Name"].AsString = fileName;
            newTextAssetBase["m_Script"].AsByteArray = fileContent; // The .msm content

            AssetFileInfo newInfo = AssetFileInfo.Create(afile, newAssetId, (int)AssetClassID.TextAsset, null);
            newInfo.SetNewData(newTextAssetBase);
            afile.Metadata.AddAssetInfo(newInfo); // Add to the file's asset list

            // Add a reference to this TextAsset in the MapBehaviour's move model list
            AssetTypeValueField newMoveEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(movesModelArray);
            newMoveEntry["Key"].AsString = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant(); // Key is lowercase move name
            newMoveEntry["Value"]["m_FileID"].AsInt = 0; // 0 for assets within the same file
            newMoveEntry["Value"]["m_PathID"].AsLong = newAssetId; // Point to the new TextAsset
            movesModelArray.Children.Add(newMoveEntry);

            // Add the new TextAsset to the AssetBundle's preload table
            AssetTypeValueField newPreloadEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newPreloadEntry["m_PathID"].AsLong = newAssetId;
            assetBundleArray.Children.Add(newPreloadEntry);
        }
    }

    private static long[] AddPictoAtlasTextureAssets(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, List<Image<Rgba32>> atlasPics, AssetTypeValueField assetBundleArray)
    {
        // Path to where PictoConverter saves the generated atlas page images
        string atlasTempFolder = Path.Combine(context.FileSystem.TempFolders.PictoFolder, "Atlas");
        // Ensure file order matches atlasPics list if indexing is implicit.
        // It's safer if PictoConverter outputs files with predictable names (e.g., atlas_0.png, atlas_1.png).
        string[] atlasImageFiles = [.. Directory.GetFiles(atlasTempFolder, "*.png").OrderBy(f => f)];

        if (atlasImageFiles.Length != atlasPics.Count)
        {
            Logger.Log($"Warning: Mismatch between expected atlas images ({atlasPics.Count}) and found files ({atlasImageFiles.Length}) in {atlasTempFolder}. This may cause issues.", LogLevel.Warning);
        }

        byte[][] encodedAtlasBytes = new byte[atlasImageFiles.Length][];

        // Encode atlas images to DXT5 in parallel
        Parallel.For(0, Math.Min(atlasImageFiles.Length, atlasPics.Count), i =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(atlasImageFiles[i]);
            int mips = 1; // No mipmaps for UI elements usually
            encodedAtlasBytes[i] = TextureImportExport.Import(image, TextureFormat.DXT5Crunched, out _, out _, ref mips)
                                   ?? throw new Exception($"Failed to encode atlas image {atlasImageFiles[i]}");
        });

        long[] atlasIDs = new long[atlasPics.Count];
        for (int i = 0; i < atlasPics.Count; i++)
        {
            if (i >= encodedAtlasBytes.Length || encodedAtlasBytes[i] == null)
            {
                Logger.Log($"Error: Encoded data for atlas page {i} is missing. Skipping asset creation.", LogLevel.Error);
                // Assign a dummy ID or handle error appropriately, e.g., by not adding to atlasIDs or throwing.
                // For now, this will lead to a PathID of 0 if not handled later.
                continue;
            }

            long newAssetId = afile.GetRandomId();
            AssetTypeValueField texBaseField = manager.CreateValueBaseField(afileInst, (int)AssetClassID.Texture2D);

            // Configure Texture2D properties
            texBaseField["m_Name"].AsString = $"sactx-{i}-{atlasPics[i].Width}x{atlasPics[i].Height}-Crunch-{context.SongData.Name}-pictoatlas";
            texBaseField["m_MipCount"].AsInt = 1;
            texBaseField["m_StreamData"]["offset"].AsULong = 0; // Not streamed
            texBaseField["m_StreamData"]["size"].AsUInt = 0;   // Not streamed
            texBaseField["m_StreamData"]["path"].AsString = ""; // Not streamed
            texBaseField["m_ForcedFallbackFormat"].AsInt = (int)TextureFormat.RGBA32; // Fallback format
            texBaseField["m_TextureFormat"].AsInt = (int)TextureFormat.DXT5Crunched;  // Primary format
            texBaseField["m_CompleteImageSize"].AsUInt = (uint)encodedAtlasBytes[i].Length;
            texBaseField["m_ImageCount"].AsInt = 1; // Single image texture
            texBaseField["m_TextureDimension"].AsInt = 2; // 2D Texture
            texBaseField["m_TextureSettings"]["m_FilterMode"].AsInt = 1; // Bilinear filtering
            texBaseField["m_TextureSettings"]["m_Aniso"].AsInt = 1;      // Anisotropic filtering level
            texBaseField["m_TextureSettings"]["m_WrapU"].AsInt = 1;      // Clamp wrapping
            texBaseField["m_TextureSettings"]["m_WrapV"].AsInt = 1;      // Clamp wrapping
            texBaseField["m_TextureSettings"]["m_WrapW"].AsInt = 1;      // Clamp wrapping (for 3D textures, but set anyway)
            texBaseField["m_ColorSpace"].AsInt = 1; // sRGB color space
            texBaseField["m_Width"].AsInt = atlasPics[i].Width;   // Atlas page width
            texBaseField["m_Height"].AsInt = atlasPics[i].Height; // Atlas page height
            texBaseField["image data"].AsByteArray = encodedAtlasBytes[i]; // The DXT5 compressed image data

            AssetFileInfo newInfo = AssetFileInfo.Create(afile, newAssetId, (int)AssetClassID.Texture2D, null);
            newInfo.SetNewData(texBaseField);
            afile.Metadata.AddAssetInfo(newInfo);
            atlasIDs[i] = newAssetId;

            // Add to AssetBundle preload table
            AssetTypeValueField newPreloadEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newPreloadEntry["m_PathID"].AsLong = newAssetId;
            assetBundleArray.Children.Add(newPreloadEntry);
        }

        return atlasIDs;
    }

    private static void AddPictoSpriteAssets(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetFileInfo spriteTemplate, AssetTypeValueField spriteAtlasBase, Dictionary<string, (int index, (int width, int height) size)> imageDict, long[] atlasIDs, AssetTypeValueField assetBundleArray)
    {
        // Get the picto names from the dictionary and sort them alphabetically.
        // This is the most important change to ensure a DETERMINISTIC build process.
        // The order of pictos in the atlas will now be consistent every time.
        var sortedPictoNames = imageDict.Keys.ToList();
        sortedPictoNames.Sort(StringComparer.InvariantCulture);

        // We must use a 'for' loop to get the index 'i'. This index is the master
        // counter that determines a picto's position within an atlas grid.
        for (int i = 0; i < sortedPictoNames.Count; i++)
        {
            string pictoName = sortedPictoNames[i];
            var (atlasPageIndex, size) = imageDict[pictoName];

            long spriteID = afile.GetRandomId();
            AssetTypeValueField spriteBaseField = manager.GetBaseField(afileInst, spriteTemplate); // Clone properties from the template

            // These "magic numbers" control sprite scaling and are dependent on the coach count.
            // This logic is preserved from the original code.
            float pixelsToUnits = context.SongData.CoachCount == 1 ? 100f : 69.140625f;
            float wMagic = context.SongData.CoachCount == 1 ? 256f : 177f;

            // --- Configure the core Sprite asset ---
            spriteBaseField["m_Name"].AsString = pictoName;
            spriteBaseField["m_Rect"]["width"].AsFloat = size.width;
            spriteBaseField["m_Rect"]["height"].AsFloat = size.height;
            spriteBaseField["m_PixelsToUnits"].AsFloat = pixelsToUnits;
            spriteBaseField["m_AtlasTags"]["Array"].Children[0].AsString = context.SongData.Name;

            // --- Configure RenderData (RD) properties ---
            // These are mostly standard for a basic sprite.
            spriteBaseField["m_RD"]["textureRect"]["x"].AsFloat = 0;
            spriteBaseField["m_RD"]["textureRect"]["y"].AsFloat = 0;
            spriteBaseField["m_RD"]["textureRect"]["width"].AsFloat = size.width;
            spriteBaseField["m_RD"]["textureRect"]["height"].AsFloat = size.height;
            spriteBaseField["m_RD"]["textureRectOffset"]["x"].AsFloat = 0;
            spriteBaseField["m_RD"]["textureRectOffset"]["y"].AsFloat = 0;
            spriteBaseField["m_RD"]["settingsRaw"].AsUInt = 0;
            spriteBaseField["m_RD"]["uvTransform"]["x"].AsFloat = pixelsToUnits;
            spriteBaseField["m_RD"]["uvTransform"]["y"].AsFloat = 256;
            spriteBaseField["m_RD"]["uvTransform"]["z"].AsFloat = pixelsToUnits;
            spriteBaseField["m_RD"]["uvTransform"]["w"].AsFloat = wMagic;

            // Generate a new, unique GUID for the RenderDataKey. This key links the Sprite to its data in the SpriteAtlas.
            uint[] guidUnity = Guid.NewGuid().ToUnity();
            spriteBaseField["m_RenderDataKey"]["first"]["data[0]"].AsUInt = guidUnity[0];
            spriteBaseField["m_RenderDataKey"]["first"]["data[1]"].AsUInt = guidUnity[1];
            spriteBaseField["m_RenderDataKey"]["first"]["data[2]"].AsUInt = guidUnity[2];
            spriteBaseField["m_RenderDataKey"]["first"]["data[3]"].AsUInt = guidUnity[3];

            // --- Configure Vertex Data ---
            // This highly-specific byte layout for vertex positions and UVs is preserved exactly from the original.
            spriteBaseField["m_RD"]["m_SubMeshes"]["Array"][0]["indexCount"].AsUInt = 6;
            spriteBaseField["m_RD"]["m_SubMeshes"]["Array"][0]["vertexCount"].AsUInt = 4;
            spriteBaseField["m_RD"]["m_IndexBuffer"]["Array"].AsByteArray = [0, 0, 1, 0, 2, 0, 2, 0, 1, 0, 3, 0];
            spriteBaseField["m_RD"]["m_VertexData"]["m_VertexCount"].AsUInt = 4;
            byte[] vertexDataBytes =
                [10, 215, 35, 192, 10, 215, 35, 64, 0, 0, 0, 0, 10, 215, 35, 64, 10, 215, 35, 64,
             0, 0, 0, 0, 10, 215, 35, 192, 10, 215, 35, 192, 0, 0, 0, 0, 10, 215, 35, 64, 10,
             215, 35, 192, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
             0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            byte[] vertexMagics = context.SongData.CoachCount == 1 ? [10, 215, 35] : [97, 247, 108];
            for (int j = 0; j <= 36; j += 12)
            {
                vertexDataBytes[j] = vertexMagics[0];
                vertexDataBytes[j + 1] = vertexMagics[1];
                vertexDataBytes[j + 2] = vertexMagics[2];
            }
            spriteBaseField["m_RD"]["m_VertexData"]["m_DataSize"].AsByteArray = vertexDataBytes;

            // Add the newly created Sprite asset to the bundle
            AssetFileInfo newSpriteInfo = AssetFileInfo.Create(afile, spriteID, (int)AssetClassID.Sprite, null);
            newSpriteInfo.SetNewData(spriteBaseField);
            afile.Metadata.AddAssetInfo(newSpriteInfo);

            // Add a reference to the new Sprite to the AssetBundle's preload table
            AssetTypeValueField newPreloadEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newPreloadEntry["m_PathID"].AsLong = spriteID;
            assetBundleArray.Children.Add(newPreloadEntry);

            // --- Update SpriteAtlas collections to include the new Sprite ---
            AssetTypeValueField packedSpriteEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_PackedSprites"]["Array"]);
            packedSpriteEntry["m_PathID"].AsLong = spriteID;
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

            // Calculate the position of this picto within the 4x4 grid of an atlas page.
            int indexInAtlasGrid = i % 16;
            int x_offset = indexInAtlasGrid % 4 * 512;
            int y_offset = indexInAtlasGrid / 4 * 512;

            if (atlasPageIndex >= atlasIDs.Length || atlasIDs[atlasPageIndex] == 0)
            {
                Logger.Log($"Error: Atlas texture for picto '{pictoName}' (atlas index {atlasPageIndex}) is invalid. Skipping RenderDataMap entry.", LogLevel.Error);
                continue;
            }

            // Set the atlas texture and the calculated rectangle for this sprite.
            renderDataEntry["second"]["texture"]["m_PathID"].AsLong = atlasIDs[atlasPageIndex];
            renderDataEntry["second"]["textureRect"]["x"].AsFloat = x_offset;
            renderDataEntry["second"]["textureRect"]["y"].AsFloat = y_offset;
            renderDataEntry["second"]["textureRect"]["width"].AsFloat = size.width;
            renderDataEntry["second"]["textureRect"]["height"].AsFloat = size.height;
            renderDataEntry["second"]["atlasRectOffset"]["x"].AsFloat = x_offset;
            renderDataEntry["second"]["atlasRectOffset"]["y"].AsFloat = y_offset;

            // These UV transform values use the offsets in a very specific, non-standard way.
            // This logic is preserved exactly to match the original output.
            renderDataEntry["second"]["uvTransform"]["x"].AsFloat = pixelsToUnits;
            renderDataEntry["second"]["uvTransform"]["y"].AsFloat = 256 + x_offset;
            renderDataEntry["second"]["uvTransform"]["z"].AsFloat = pixelsToUnits;
            renderDataEntry["second"]["uvTransform"]["w"].AsFloat = wMagic + y_offset;

            renderDataEntry["second"]["downscaleMultiplier"].AsFloat = 1;
            renderDataEntry["second"]["settingsRaw"].AsUInt = 3;

            spriteAtlasBase["m_RenderDataMap"]["Array"].Children.Add(renderDataEntry);
        }
    }

    private static void FinalizeSpriteAtlas(AssetsFile afile, AssetFileInfo spriteAtlasInfo, AssetTypeValueField spriteAtlasBase, AssetFileInfo spriteTemplate)
    {
        spriteAtlasInfo.SetNewData(spriteAtlasBase); // Apply changes to the SpriteAtlas asset
        afile.AssetInfos.Remove(spriteTemplate); // Remove the original template sprite, it's no longer needed
    }

    private static void PopulateDanceDataClips(ConversionContext context, AssetTypeValueField mapBase, Dictionary<string, (int index, (int Width, int Height) size)> imageDict)
    {
        AssetTypeValueField motionClipsArray = mapBase["DanceData"]["MotionClips"]["Array"];
        AssetTypeValueField goldEffectClipsArray = mapBase["DanceData"]["GoldEffectClips"]["Array"];
        AssetTypeValueField hideHudClipsArray = mapBase["DanceData"]["HideHudClips"]["Array"];
        AssetTypeValueField pictoClipsArray = mapBase["DanceData"]["PictoClips"]["Array"];

        // Clear existing clips before repopulating
        motionClipsArray.Children.Clear();
        goldEffectClipsArray.Children.Clear();
        hideHudClipsArray.Children.Clear();
        pictoClipsArray.Children.Clear();

        foreach (IClip iClip in context.SongData.Clips) // Iterate through all clips from parsed song data
        {
            switch (iClip)
            {
                case GoldEffectClip clip:
                    AssetTypeValueField newGold = ValueBuilder.DefaultValueFieldFromArrayTemplate(goldEffectClipsArray);
                    newGold["StartTime"].AsInt = clip.StartTime;
                    newGold["Duration"].AsInt = clip.Duration;
                    newGold["GoldEffectType"].AsInt = clip.EffectType; // Type of gold effect
                    newGold["Id"].AsLong = clip.Id;
                    newGold["TrackId"].AsLong = clip.TrackId;
                    newGold["IsActive"].AsUInt = (uint)clip.IsActive;
                    goldEffectClipsArray.Children.Add(newGold);
                    break;
                case PictogramClip clip:
                    AssetTypeValueField newPicto = ValueBuilder.DefaultValueFieldFromArrayTemplate(pictoClipsArray);
                    string pictoName = Path.GetFileNameWithoutExtension(clip.PictoPath);
                    // Case-insensitive lookup for picto name in dictionary
                    if (!imageDict.ContainsKey(pictoName))
                    {
                        string? foundKey = imageDict.Keys.FirstOrDefault(k => k.Equals(pictoName, StringComparison.InvariantCultureIgnoreCase));
                        if (foundKey != null)
                            pictoName = foundKey;
                        else
                            Logger.Log($"Pictogram '{pictoName}' for clip not found in image dictionary. Clip might not display correctly.", LogLevel.Warning);
                    }

                    newPicto["StartTime"].AsInt = clip.StartTime;
                    newPicto["Duration"].AsInt = clip.Duration == 0 ? 16 : clip.Duration; // Ensure non-zero duration, 16 is a small default
                    newPicto["Id"].AsLong = clip.Id;
                    newPicto["TrackId"].AsLong = clip.TrackId;
                    newPicto["IsActive"].AsUInt = (uint)clip.IsActive;
                    newPicto["PictoPath"].AsString = pictoName; // Name of the picto sprite
                    newPicto["CoachCount"].AsUInt = (uint)clip.CoachCount; // Number of coaches this picto applies to
                    pictoClipsArray.Children.Add(newPicto);
                    break;
                case MotionClip clip:
                    // Skip if not an .msm move or coach ID is out of bounds
                    if (!clip.ClassifierPath.EndsWith(".msm") || clip.CoachId >= context.SongData.CoachCount)
                        continue;
                    AssetTypeValueField newMotion = ValueBuilder.DefaultValueFieldFromArrayTemplate(motionClipsArray);
                    newMotion["StartTime"].AsInt = clip.StartTime;
                    newMotion["Duration"].AsInt = clip.Duration;
                    newMotion["Id"].AsLong = clip.Id;
                    newMotion["TrackId"].AsLong = clip.TrackId;
                    newMotion["IsActive"].AsUInt = (uint)clip.IsActive;
                    newMotion["MoveName"].AsString = Path.GetFileNameWithoutExtension(clip.ClassifierPath).ToLowerInvariant(); // Move name (from .msm file)
                    newMotion["GoldMove"].AsUInt = (uint)clip.GoldMove; // 0 for standard, 1 for gold move
                    newMotion["CoachId"].AsInt = clip.CoachId;     // Which coach performs this move
                    newMotion["MoveType"].AsInt = clip.MoveType;   // Type of move (e.g., classic, sweat)
                    newMotion["Color"].AsString = ""; // Optional color override for the move, usually empty
                    motionClipsArray.Children.Add(newMotion);
                    break;
                case HideUserInterfaceClip clip:
                    AssetTypeValueField newHideHud = ValueBuilder.DefaultValueFieldFromArrayTemplate(hideHudClipsArray);
                    newHideHud["StartTime"].AsInt = clip.StartTime;
                    newHideHud["Duration"].AsInt = clip.Duration;
                    newHideHud["IsActive"].AsUInt = (uint)clip.IsActive;
                    // Add other fields if HideUserInterfaceClip has them, e.g., EventType, TargetElements
                    // newHideHud["EventType"].AsInt = clip.EventType; // Example
                    hideHudClipsArray.Children.Add(newHideHud);
                    break;
            }
        }
    }

    private static void UpdateCoachCounters(ConversionContext context, AssetTypeValueField mapBase)
    {
        AssetTypeValueField handCoachCounters = mapBase["HandOnlyCoachDatas"]["Array"];
        AssetTypeValueField bodyCoachCounters = mapBase["FullBodyCoachDatas"]["Array"]; // If this field exists and is used

        if (bodyCoachCounters == null || bodyCoachCounters.IsDummy) // Check if bodyCoachCounters is valid
        {
            bodyCoachCounters = ValueBuilder.DefaultValueFieldFromArrayTemplate(handCoachCounters); // If not, use handCoachCounters as a template
        }

        // Clear existing counters
        handCoachCounters.Children.Clear();
        bodyCoachCounters.Children.Clear(); 

        // Initialize counters for each coach
        for (int i = 0; i < context.SongData.CoachCount; i++)
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

        // Recalculate move counts based on the MotionClips added
        foreach (var motionClipField in mapBase["DanceData"]["MotionClips"]["Array"].Children)
        {
            int coachId = motionClipField["CoachId"].AsInt;
            int moveType = motionClipField["MoveType"].AsInt;
            bool isGoldMove = motionClipField["GoldMove"].AsUInt == 1;

            if (coachId >= 0 && coachId < handCoachCounters.Children.Count) // Ensure coachId is valid
            {
                if (moveType == 0) // Hand moves
                {
                    if (isGoldMove)
                        handCoachCounters.Children[coachId]["GoldMovesCount"].AsUInt++;
                    else
                        handCoachCounters.Children[coachId]["StandardMovesCount"].AsUInt++;
                }
                else if (moveType == 1) // Body moves
                {
                    if (isGoldMove)
                        bodyCoachCounters.Children[coachId]["GoldMovesCount"].AsUInt++;
                    else
                        bodyCoachCounters.Children[coachId]["StandardMovesCount"].AsUInt++;
                }
            }
        }
    }

    private static void FinalizeAndSaveBundle(ConversionContext context, AssetBundleFile bun, AssetsFile afile,
        AssetTypeValueField musicTrackBase, AssetTypeValueField mapBase, AssetTypeValueField assetBundleBase,
        Action setMusicTrackData, Action setMapData, Action setAssetBundleData)
    {
        // Apply changes to the MonoBehaviour assets
        setMusicTrackData();
        setMapData();

        // The AssetBundle's m_Container lists main assets. Its 'preloadSize' field for these entries
        // should be updated to reflect the total number of assets in the m_PreloadTable.
        AssetTypeValueField preloadTableArray = assetBundleBase["m_PreloadTable"]["Array"];
        AssetTypeValueField containerArray = assetBundleBase["m_Container"]["Array"];
        if (containerArray != null && !containerArray.IsDummy)
        {
            // Typically, the first few entries in m_Container (for MusicTrack, MapBehaviour)
            // need their preloadSize updated.
            for (int i = 0; i < Math.Min(2, containerArray.Children.Count); i++)
            {
                containerArray[i]["second"]["preloadSize"].AsInt = preloadTableArray.Children.Count;
            }
        }

        setAssetBundleData(); // Apply changes to the AssetBundle asset itself

        // Write changes back to the bundle file structure
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Save and compress the modified bundle
        string outputPackagePath = context.FileSystem.OutputFolders.MapPackageFolder;
        bun.SaveAndCompress(outputPackagePath, context.Request.ExportType == ExportType.CustomServer);
    }
}