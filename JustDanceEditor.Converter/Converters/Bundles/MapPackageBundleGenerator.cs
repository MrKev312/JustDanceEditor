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
        var bundleLoadData = InitializeBundle(context);
        var manager = bundleLoadData.Manager;
        var afileInst = bundleLoadData.AFileInst;
        var afile = bundleLoadData.AFile;
        var bun = bundleLoadData.BunFile;
        var sortedAssetInfos = bundleLoadData.SortedAssetInfos;

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
            () => bundleLoadData.MusicTrackInfo.SetNewData(musicTrackBase),
            () => bundleLoadData.MapInfo.SetNewData(mapBase),
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

        AssetFileInfo[] musicTrackInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour).ToArray();
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
        AssetFileInfo[] monoBehaviourInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour).ToArray();
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
        foreach (var entry in preloadEntriesToRemove.Distinct()) assetBundleArray.Children.Remove(entry);
        foreach (var asset in assetsToRemove.Distinct()) afile.AssetInfos.Remove(asset);

        if (spriteTemplate == null) throw new Exception("Sprite template for pictos not found! Ensure the template bundle contains at least one sprite.");
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
        string[] atlasImageFiles = Directory.GetFiles(atlasTempFolder, "*.png").OrderBy(f => f).ToArray();

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
        // Assumes individual picto images are temporarily saved by PictoConverter if needed for reference,
        // or that imageDict contains all necessary metadata (like packing rects on atlas pages).
        // This iteration is over the keys of imageDict, which represent individual pictos.
        foreach (var pictoEntry in imageDict)
        {
            string pictoName = pictoEntry.Key;
            var pictoData = pictoEntry.Value; // Contains atlas page index and original picto dimensions

            long spriteID = afile.GetRandomId();
            AssetTypeValueField spriteBaseField = manager.GetBaseField(afileInst, spriteTemplate); // Clone from template

            // Configure sprite properties based on picto data
            float pixelsToUnits = context.SongData.CoachCount == 1 ? 100f : 69.140625f; // Game-specific scaling
            // `wMagic` seems to be related to UV or rect width calculation, depends on coach count.
            float wMagic = context.SongData.CoachCount == 1 ? 256f : 177f;

            spriteBaseField["m_Name"].AsString = pictoName;
            // m_Rect is the sprite's rectangle in its own coordinate system (usually at 0,0 with its own width/height).
            spriteBaseField["m_Rect"]["width"].AsFloat = pictoData.size.width;
            spriteBaseField["m_Rect"]["height"].AsFloat = pictoData.size.height;

            // m_RD.textureRect defines the source rectangle on the *original, non-atlased* sprite image if it were standalone.
            // For atlased sprites, this often remains the full sprite size (0,0,width,height).
            // The actual UVs mapping to the atlas page are handled by SpriteAtlas data or m_RD.m_VertexData.
            spriteBaseField["m_RD"]["textureRect"]["x"].AsFloat = 0;
            spriteBaseField["m_RD"]["textureRect"]["y"].AsFloat = 0;
            spriteBaseField["m_RD"]["textureRect"]["width"].AsFloat = pictoData.size.width;
            spriteBaseField["m_RD"]["textureRect"]["height"].AsFloat = pictoData.size.height;
            spriteBaseField["m_RD"]["textureRectOffset"]["x"].AsFloat = 0; // Offset if the sprite was trimmed (pivot adjustment)
            spriteBaseField["m_RD"]["textureRectOffset"]["y"].AsFloat = 0;
            spriteBaseField["m_RD"]["settingsRaw"].AsUInt = 0; // Sprite packing settings (e.g., tight packing)

            // m_RD.uvTransform: This field can be complex. For non-atlased sprites, it might scale/offset UVs.
            // For atlased sprites made through Unity's packer, this might be identity or related to the original texture.
            // The values here (pixelsToUnits, 256, etc.) are specific and seem to be a custom use.
            spriteBaseField["m_RD"]["uvTransform"]["x"].AsFloat = pixelsToUnits;
            spriteBaseField["m_RD"]["uvTransform"]["y"].AsFloat = 256;
            spriteBaseField["m_RD"]["uvTransform"]["z"].AsFloat = pixelsToUnits;
            spriteBaseField["m_RD"]["uvTransform"]["w"].AsFloat = wMagic;

            spriteBaseField["m_AtlasTags"]["Array"].Children[0].AsString = context.SongData.Name; // Tag for atlas packing
            spriteBaseField["m_PixelsToUnits"].AsFloat = pixelsToUnits; // Standard Unity sprite property

            // Generate a new GUID for m_RenderDataKey, used by SpriteAtlas.
            uint[] guidUnity = Guid.NewGuid().ToUnity();
            spriteBaseField["m_RenderDataKey"]["first"]["data[0]"].AsUInt = guidUnity[0];
            spriteBaseField["m_RenderDataKey"]["first"]["data[1]"].AsUInt = guidUnity[1];
            spriteBaseField["m_RenderDataKey"]["first"]["data[2]"].AsUInt = guidUnity[2];
            spriteBaseField["m_RenderDataKey"]["first"]["data[3]"].AsUInt = guidUnity[3];

            // Vertex data (positions, UVs) configuration. This is highly engine-specific.
            // Standard quad: 4 vertices, 6 indices.
            spriteBaseField["m_RD"]["m_SubMeshes"]["Array"][0]["indexCount"].AsUInt = 6;
            spriteBaseField["m_RD"]["m_SubMeshes"]["Array"][0]["vertexCount"].AsUInt = 4;
            spriteBaseField["m_RD"]["m_IndexBuffer"]["Array"].AsByteArray = [0, 0, 1, 0, 2, 0, 2, 0, 1, 0, 3, 0]; // Standard quad indices (0,1,2, 2,1,3)
            spriteBaseField["m_RD"]["m_VertexData"]["m_VertexCount"].AsUInt = 4;

            // The m_VertexData.m_DataSize byte array is critical and contains interleaved vertex attributes (pos, uv, color etc.).
            // Its structure depends on the vertex format used by the shader/engine for these sprites.
            // This byte array is initialized with a template/default value.
            byte[] vertexDataBytes =
                [10, 215, 35, 192, 10, 215, 35, 64, 0, 0, 0, 0, 10, 215, 35, 64, 10, 215, 35, 64,
                 0, 0, 0, 0, 10, 215, 35, 192, 10, 215, 35, 192, 0, 0, 0, 0, 10, 215, 35, 64, 10,
                 215, 35, 192, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            spriteBaseField["m_RD"]["m_VertexData"]["m_DataSize"].AsByteArray = vertexDataBytes;

            // Specific bytes in vertexDataBytes are modified based on coach count.
            // These "magic bytes" likely affect vertex positions or UVs in a shader-specific way.
            byte[] vertexMagics = context.SongData.CoachCount == 1 ? [10, 215, 35] : [97, 247, 108];
            for (int j = 0; j <= 36; j += 12) // Modify specific byte sequences in the vertex data
            {
                vertexDataBytes[j] = vertexMagics[0];
                vertexDataBytes[j + 1] = vertexMagics[1];
                vertexDataBytes[j + 2] = vertexMagics[2];
            }
            // Re-assign if AsByteArray returned a copy, otherwise modification is in-place.
            spriteBaseField["m_RD"]["m_VertexData"]["m_DataSize"].AsByteArray = vertexDataBytes;

            AssetFileInfo newSpriteInfo = AssetFileInfo.Create(afile, spriteID, (int)AssetClassID.Sprite, null);
            newSpriteInfo.SetNewData(spriteBaseField);
            afile.Metadata.AddAssetInfo(newSpriteInfo);

            AssetTypeValueField newPreloadEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newPreloadEntry["m_PathID"].AsLong = spriteID;
            assetBundleArray.Children.Add(newPreloadEntry);

            // --- Update SpriteAtlas collections ---
            // Add PPtr to this sprite in m_PackedSprites
            AssetTypeValueField packedSpriteEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_PackedSprites"]["Array"]);
            packedSpriteEntry["m_PathID"].AsLong = spriteID;
            spriteAtlasBase["m_PackedSprites"]["Array"].Children.Add(packedSpriteEntry);

            // Add sprite name to m_PackedSpriteNamesToIndex (maps name to index in m_PackedSprites)
            AssetTypeValueField packedSpriteNameEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"]);
            packedSpriteNameEntry.AsString = pictoName;
            spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"].Children.Add(packedSpriteNameEntry);

            // Add to m_RenderDataMap (maps RenderDataKey to actual render data on atlas)
            AssetTypeValueField renderDataEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_RenderDataMap"]["Array"]);
            // Key part 1: The GUID from sprite's m_RenderDataKey
            renderDataEntry["first"]["first"]["data[0]"].AsUInt = guidUnity[0];
            renderDataEntry["first"]["first"]["data[1]"].AsUInt = guidUnity[1];
            renderDataEntry["first"]["first"]["data[2]"].AsUInt = guidUnity[2];
            renderDataEntry["first"]["first"]["data[3]"].AsUInt = guidUnity[3];
            // Key part 2: A long value, seems to be a type identifier or fixed value for sprites.
            renderDataEntry["first"]["second"].AsLong = 21300000;

            // Value part: SpriteRenderData for the atlas
            // This defines the sprite's PPtr to its atlas texture page, and its rectangle on that page.
            if (pictoData.index >= atlasIDs.Length || atlasIDs[pictoData.index] == 0)
            {
                Logger.Log($"Error: Atlas texture for picto '{pictoName}' (atlas index {pictoData.index}) is invalid. Skipping RenderDataMap entry.", LogLevel.Error);
                continue;
            }
            renderDataEntry["second"]["texture"]["m_PathID"].AsLong = atlasIDs[pictoData.index];

            // textureRect: The sprite's rectangle (x, y, width, height) on the atlas texture page.
            // This information MUST come from the PictoConverter's packing step.
            // Assuming PictoConverter provides (x, y) offsets if it packs tightly.
            // If PictoConverter just outputs full atlas pages, and each picto is on one such page,
            // and this method is supposed to calculate sub-rects, that logic is missing here.
            // For now, assuming x,y are 0 if PictoConverter already placed them on the atlas page image.
            // This part is highly dependent on how PictoConverter outputs atlas pages and picto locations.
            // The values below are placeholders or simplistic assumptions.
            renderDataEntry["second"]["textureRect"]["x"].AsFloat = 0; // Needs actual X offset on atlas page from packer
            renderDataEntry["second"]["textureRect"]["y"].AsFloat = 0; // Needs actual Y offset on atlas page from packer
            renderDataEntry["second"]["textureRect"]["width"].AsFloat = pictoData.size.width;
            renderDataEntry["second"]["textureRect"]["height"].AsFloat = pictoData.size.height;

            // atlasRectOffset seems to be similar to textureRect's x,y for offset purposes.
            renderDataEntry["second"]["atlasRectOffset"]["x"].AsFloat = 0; // Needs actual X offset
            renderDataEntry["second"]["atlasRectOffset"]["y"].AsFloat = 0; // Needs actual Y offset

            // uvTransform for SpriteAtlas render data:
            // These values are crucial for correctly mapping UVs from the sprite's quad to the atlas.
            // (X: scaleX, Y: offsetY_transformed, Z: scaleZ_usuallySameAsX, W: offsetX_transformed)
            // The exact calculation depends on atlas texture size and sprite rect on it.
            // The previous `pixelsToUnits, 256 + x_offset, pixelsToUnits, wMagic + y_offset` suggests a custom transform.
            // `x_offset` and `y_offset` here would be the pixel offsets of the sprite on its atlas page.
            // Without correct packer-provided offsets, these will be inaccurate.
            // Assuming 0 offsets for now.
            float x_offset_on_atlas = 0; // This should be from PictoConverter packing data
            float y_offset_on_atlas = 0; // This should be from PictoConverter packing data
            renderDataEntry["second"]["uvTransform"]["x"].AsFloat = pixelsToUnits;
            renderDataEntry["second"]["uvTransform"]["y"].AsFloat = 256 + x_offset_on_atlas;
            renderDataEntry["second"]["uvTransform"]["z"].AsFloat = pixelsToUnits;
            renderDataEntry["second"]["uvTransform"]["w"].AsFloat = wMagic + y_offset_on_atlas;

            renderDataEntry["second"]["downscaleMultiplier"].AsFloat = 1; // No downscaling
            renderDataEntry["second"]["settingsRaw"].AsUInt = 3; // Settings for atlased sprite

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