﻿using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using AssetsTools.NET;

using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.UbiArt.Tapes;
using JustDanceEditor.Converter.Unity.TextureConverter;
using JustDanceEditor.Converter.Unity;

using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

namespace JustDanceEditor.Converter.Converters.Bundles;
public static class MapPackageBundleGenerator
{
    public static void GenerateMapPackage(ConvertUbiArtToUnity convert)
    {
        // Get the mapPackage path
        // /template/cachex/MapPackage/*
        string mapPackagePath = Directory.GetFiles(Path.Combine(convert.TemplateXFolder, "MapPackage"))[0];

        // Convert the pictos in /cache/itf_cooked/nx/world/maps/{mapName}/timeline/pictos
        Task<(Dictionary<string, int>, List<Image<Rgba32>>)> pictoTask =
            Task.Run(() => Task.FromResult(PictoConverter.ConvertPictos(convert)));

        // While the pictos are in the oven, we can convert the mapfiles
        Console.WriteLine("Converting MapPackage...");
        // Open the mapPackage using AssetTools.NET
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(mapPackagePath, true);
        AssetBundleFile bun = bunInst.file;
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        // Get the MonoBehaviour that is not named "MusicTrack"
        AssetFileInfo[] musicTrackInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour).ToArray();
        AssetTypeValueField musicTrackBase;
        AssetTypeValueField mapBase;

        // Get the assetbundle
        AssetFileInfo assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundle);

        // Set the name of the assetbundle to {mapName}_MapPackage
        assetBundleBase["m_Name"].AsString = $"{convert.SongData.Name}_MapPackage";
        assetBundleBase["m_AssetBundleName"].AsString = $"{convert.SongData.Name}_MapPackage";

        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        {
            AssetTypeValueField firstTrackInfo = manager.GetBaseField(afileInst, musicTrackInfos[0]);
            string name = firstTrackInfo["m_Name"].AsString;

            bool isFirstTrackMusicTrack = name == "MusicTrack";

            musicTrackBase = isFirstTrackMusicTrack ? firstTrackInfo : manager.GetBaseField(afileInst, musicTrackInfos[1]);
            mapBase = isFirstTrackMusicTrack ? manager.GetBaseField(afileInst, musicTrackInfos[1]) : firstTrackInfo;

            if (!isFirstTrackMusicTrack)
            {
                (musicTrackInfos[0], musicTrackInfos[1]) = (musicTrackInfos[1], musicTrackInfos[0]);
            }
        }

        // Set the name of the mapBase to the name of the map
        mapBase["m_Name"].AsString = convert.SongData.Name;
        mapBase["MapName"].AsString = convert.SongData.Name;
        mapBase["SongDesc"]["MapName"].AsString = convert.SongData.Name;
        mapBase["SongDesc"]["NumCoach"].AsInt = convert.SongData.CoachCount;
        mapBase["KaraokeData"]["MapName"].AsString = convert.SongData.Name;
        mapBase["DanceData"]["MapName"].AsString = convert.SongData.Name;

        // Remove all the old dance moves
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.TextAsset))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);
            string name = assetBase["m_Name"].AsString;

            // If it doesn't end with ".msm", skip it
            if (!name.EndsWith(".msm"))
                continue;

            // Then remove it from the bundle
            afile.AssetInfos.Remove(assetInfo);

            // And remove it from the preload table
            assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId).First());
        }

        // Remove all the old pictos
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);

            // Then remove it from the bundle
            afile.AssetInfos.Remove(assetInfo);

            // And remove it from the preload table
            assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId).First());
        }

        // The template sprite from which all custom sprites are based
        AssetFileInfo? spriteTemplate = null;

        // Also remove their corresponding Sprites
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);

            // Remove it from the preload table
            assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId).First());

            // Set the template sprite
            if (spriteTemplate is null)
            {
                spriteTemplate = assetInfo;

                // This one is the template, so we keep it
                continue;
            }

            // Else, remove it from the bundle
            afile.AssetInfos.Remove(assetInfo);
        }

        // If the sprite atlas is still null, throw an exception
        if (spriteTemplate is null)
            throw new Exception("Sprite template is null!");

        // Store reference to the SpriteAtlas
        AssetFileInfo spriteAtlasInfo = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.SpriteAtlas).First();
        AssetTypeValueField spriteAtlasBase = manager.GetBaseField(afileInst, spriteAtlasInfo);
        spriteAtlasBase["m_Name"].AsString = convert.SongData.Name;
        spriteAtlasBase["m_Tag"].AsString = convert.SongData.Name;

        // Empty the packedSprites, packedSpriteNamesToIndex and RenderDataMap arrays
        spriteAtlasBase["m_PackedSprites"]["Array"].Children.Clear();
        spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"].Children.Clear();
        spriteAtlasBase["m_RenderDataMap"]["Array"].Children.Clear();

        /// The musicTrackBase:
        // First set the basic fields
        Structure trackStructure = convert.SongData.MTrack.COMPONENTS[0].trackData.structure;
        AssetTypeValueField structure = musicTrackBase["m_structure"]["MusicTrackStructure"];
        structure["startBeat"].AsInt = trackStructure.startBeat;
        structure["endBeat"].AsInt = trackStructure.endBeat;
        structure["videoStartTime"].AsDouble = trackStructure.videoStartTime;
        structure["previewEntry"].AsDouble = trackStructure.previewEntry;
        structure["previewLoopStart"].AsDouble = trackStructure.previewLoopStart;
        structure["previewLoopEnd"].AsDouble = 0;
        structure["previewDuration"].AsDouble = trackStructure.previewLoopEnd - trackStructure.previewLoopStart;

        // Set the signatures array
        AssetTypeValueField signaturesArray = structure["signatures"]["Array"];
        signaturesArray.Children.Clear();

        foreach (Signature signature in trackStructure.signatures)
        {
            AssetTypeValueField newSignature = ValueBuilder.DefaultValueFieldFromArrayTemplate(signaturesArray);
            AssetTypeValueField musicSignature = newSignature["MusicSignature"];

            musicSignature["beats"].AsInt = signature.beats;
            musicSignature["marker"].AsDouble = signature.marker;
            musicSignature["comment"].AsString = "";

            signaturesArray.Children.Add(newSignature);
        }

        // Set the markers array
        AssetTypeValueField markersArray = structure["markers"]["Array"];
        markersArray.Children.Clear();

        foreach (int marker in trackStructure.markers)
        {
            AssetTypeValueField newMarker = ValueBuilder.DefaultValueFieldFromArrayTemplate(markersArray);

            newMarker["VAL"].AsLong = marker;

            markersArray.Children.Add(newMarker);
        }

        // Set the sections array
        AssetTypeValueField sectionsArray = structure["sections"]["Array"];
        sectionsArray.Children.Clear();

        foreach (Section section in trackStructure.sections)
        {
            AssetTypeValueField newSection = ValueBuilder.DefaultValueFieldFromArrayTemplate(sectionsArray);
            AssetTypeValueField musicSection = newSection["MusicSection"];

            musicSection["sectionType"].AsInt = section.sectionType;
            musicSection["marker"].AsDouble = section.marker;
            musicSection["comment"].AsString = "";

            sectionsArray.Children.Add(newSection);
        }

        /// The mapBase:
        // Empty the karaoke data
        AssetTypeValueField karaokeArray = mapBase["KaraokeData"]["Clips"]["Array"];
        karaokeArray.Children.Clear();

        // For each clip in the karaoke file, create a new KaraokeClipContainer
        foreach (KaraokeClip clip in convert.SongData.KTape.Clips)
        {
            // Create a new KaraokeClipContainer
            AssetTypeValueField newContainer = ValueBuilder.DefaultValueFieldFromArrayTemplate(karaokeArray);
            AssetTypeValueField karaokeClip = newContainer["KaraokeClip"];

            // Set the fields
            karaokeClip["StartTime"].AsInt = clip.StartTime;
            karaokeClip["Duration"].AsInt = clip.Duration;
            karaokeClip["Lyrics"].AsString = clip.Lyrics;
            karaokeClip["IsActive"].AsUInt = (uint)clip.IsActive;
            karaokeClip["TrackId"].AsLong = clip.TrackId;
            karaokeClip["Pitch"].AsFloat = clip.Pitch;
            karaokeClip["IsEndOfLine"].AsUInt = (uint)clip.IsEndOfLine;
            karaokeClip["ContentType"].AsInt = 2;
            karaokeClip["Id"].AsLong = clip.Id;
            karaokeClip["SemitoneTolerance"].AsInt = (int)clip.SemitoneTolerance;
            karaokeClip["StartTimeTolerance"].AsInt = clip.StartTimeTolerance;
            karaokeClip["EndTimeTolerance"].AsInt = clip.EndTimeTolerance;

            // Add the new KaraokeClipContainer to the array
            karaokeArray.Children.Add(newContainer);
        }

        // Empty the moves data
        AssetTypeValueField movesArray = mapBase["HandDeviceMoveModels"]["list"]["Array"];
        movesArray.Children.Clear();

        // Add the new dance moves
        string[] moveFiles = Directory.GetFiles(convert.MovesFolder);
        foreach (string item in moveFiles)
        {
            // Get the file name and content, must read as bytes
            string fileName = Path.GetFileName(item);
            byte[] fileContent = File.ReadAllBytes(item);

            // Random new asset id
            long newAssetId = afile.GetRandomId();

            AssetTypeValueField newBaseField = manager.CreateValueBaseField(afileInst, (int)AssetClassID.TextAsset);

            // Then set the name and content
            newBaseField["m_Name"].AsString = fileName;
            newBaseField["m_Script"].AsByteArray = fileContent;

            // Make a new AssetFileInfo
            AssetFileInfo newInfo = AssetFileInfo.Create(afile, newAssetId, (int)AssetClassID.TextAsset, null);
            newInfo.SetNewData(newBaseField);

            // Add the new AssetFileInfo to the AssetsFile
            afile.Metadata.AddAssetInfo(newInfo);

            // Build up the reference
            AssetTypeValueField newMove = ValueBuilder.DefaultValueFieldFromArrayTemplate(movesArray);
            newMove["Key"].AsString = Path.GetFileNameWithoutExtension(fileName);
            newMove["Value"]["m_FileID"].AsInt = 0;
            newMove["Value"]["m_PathID"].AsLong = newAssetId;

            // Add the new move reference to the array
            movesArray.Children.Add(newMove);

            // Add the new move reference to the preload table
            AssetTypeValueField newAssetBundle = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newAssetBundle["m_PathID"].AsLong = newAssetId;
            assetBundleArray.Children.Add(newAssetBundle);
        }

        // Wait for the pictos to finish
        (Dictionary<string, int> imageDict, List<Image<Rgba32>> atlasPics) = pictoTask.Result;

        // Add the new pictos
        string[] FileDirs = Directory.GetFiles(Path.Combine(convert.TempPictoFolder, "Atlas"));
        byte[][] endImageBytes = new byte[FileDirs.Length][];

        Parallel.For(0, FileDirs.Length, i =>
        {
            TextureFormat fmt = TextureFormat.DXT5Crunched;
            byte[] platformBlob = [];
            uint platform = afile.Metadata.TargetPlatform;
            int mips = 1;
            string path = FileDirs[i];

            // Load the image
            Image<Rgba32> image = Image.Load<Rgba32>(path);

            byte[] imageBytes = TextureImportExport.Import(image, fmt, out int width, out int height, ref mips, platform, platformBlob) ?? throw new Exception("Failed to encode image!");

            // Add the image bytes to the array
            endImageBytes[i] = imageBytes;
        });

        // First add all atlas images to the bundle
        long[] atlasIDs = new long[atlasPics.Count];

        // TODO for loop for atlasPics
        for (int i = 0; i < atlasPics.Count; i++)
        {
            // Get the endImageBytes
            byte[] imgBytes = endImageBytes[i];

            // Create a new asset id
            long newAssetId = afile.GetRandomId();

            // Create a new AssetTypeValueField
            AssetTypeValueField texBaseField = manager.CreateValueBaseField(afileInst, (int)AssetClassID.Texture2D);

            // Set the name and content
            texBaseField["m_Name"].AsString = $"sactx-{i}-{2048}x{2048}-Crunch-{convert.SongData.Name}-5e98ca96";
            texBaseField["m_MipCount"].AsInt = 1;

            texBaseField["m_MipCount"].AsInt = 1;

            AssetTypeValueField m_StreamData = texBaseField["m_StreamData"];
            m_StreamData["offset"].AsInt = 0;
            m_StreamData["size"].AsInt = 0;
            m_StreamData["path"].AsString = "";

            texBaseField["m_ForcedFallbackFormat"].AsInt = (int)TextureFormat.RGBA32;
            texBaseField["m_TextureFormat"].AsInt = (int)TextureFormat.DXT5Crunched;
            texBaseField["m_CompleteImageSize"].AsUInt = (uint)endImageBytes[i].Length;
            texBaseField["m_ImageCount"].AsInt = 1;
            texBaseField["m_TextureDimension"].AsInt = 2;

            texBaseField["m_TextureSettings"]["m_FilterMode"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_Aniso"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapU"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapV"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapW"].AsInt = 1;

            texBaseField["m_ColorSpace"].AsInt = 1;

            texBaseField["m_Width"].AsInt = 2048;
            texBaseField["m_Height"].AsInt = 2048;

            AssetTypeValueField image_data = texBaseField["image data"];
            image_data.Value.ValueType = AssetValueType.ByteArray;
            image_data.TemplateField.ValueType = AssetValueType.ByteArray;
            image_data.AsByteArray = imgBytes;

            // Make a new AssetFileInfo
            AssetFileInfo newInfo = AssetFileInfo.Create(afile, newAssetId, (int)AssetClassID.Texture2D, null);
            newInfo.SetNewData(texBaseField);

            // Add the new AssetFileInfo to the AssetsFile
            afile.Metadata.AddAssetInfo(newInfo);

            // Add the id to the array
            atlasIDs[i] = newAssetId;

            // Add the new move reference to the preload table
            AssetTypeValueField newAssetBundle = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newAssetBundle["m_PathID"].AsLong = newAssetId;
            assetBundleArray.Children.Add(newAssetBundle);
        }

        // Then add all the pictos to the bundle
        FileDirs = Directory.GetFiles(convert.TempPictoFolder);

        for (int i = 0; i < FileDirs.Length; i++)
        {
            string item = FileDirs[i];

            // Set each picto as it's own asset
            string pictoName = Path.GetFileNameWithoutExtension(item);

            // Now we create a new Sprite
            long spriteID = afile.GetRandomId();

            // Load in the sprite template
            AssetTypeValueField spriteBaseField = manager.GetBaseField(afileInst, spriteTemplate);

            // Set the name and content
            spriteBaseField["m_Name"].AsString = pictoName;
            spriteBaseField["m_Rect"]["width"].AsFloat = 512;
            spriteBaseField["m_Rect"]["height"].AsFloat = 512;
            spriteBaseField["m_RD"]["textureRect"]["width"].AsFloat = 512;
            spriteBaseField["m_RD"]["textureRect"]["height"].AsFloat = 512;
            spriteBaseField["m_AtlasTags"]["Array"].Children[0].AsString = convert.SongData.Name;

            uint[] uintArray = Guid.NewGuid().ToUnity();

            // Use the GUID for the texture as the key
            spriteBaseField["m_RenderDataKey"]["first"]["data[0]"].AsUInt = uintArray[0];
            spriteBaseField["m_RenderDataKey"]["first"]["data[1]"].AsUInt = uintArray[1];
            spriteBaseField["m_RenderDataKey"]["first"]["data[2]"].AsUInt = uintArray[2];
            spriteBaseField["m_RenderDataKey"]["first"]["data[3]"].AsUInt = uintArray[3];

            // Add the new Sprite to the AssetsFile
            AssetFileInfo newSpriteInfo = AssetFileInfo.Create(afile, spriteID, (int)AssetClassID.Sprite, null);
            newSpriteInfo.SetNewData(spriteBaseField);

            // Add the new AssetFileInfo to the AssetsFile
            afile.Metadata.AddAssetInfo(newSpriteInfo);

            // Add the new move reference to the preload table
            AssetTypeValueField newAssetBundle = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newAssetBundle["m_PathID"].AsLong = spriteID;
            assetBundleArray.Children.Add(newAssetBundle);

            // Add it to the SpriteAtlas
            AssetTypeValueField newPackedSprite = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_PackedSprites"]["Array"]);
            newPackedSprite["m_PathID"].AsLong = spriteID;
            spriteAtlasBase["m_PackedSprites"]["Array"].Children.Add(newPackedSprite);

            // Add it to the packedSpriteNamesToIndex
            AssetTypeValueField newPackedSpriteName = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"]);
            newPackedSpriteName.AsString = pictoName;
            spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"].Children.Add(newPackedSpriteName);

            // Add it to the RenderDataMap
            AssetTypeValueField newRenderDataMap = ValueBuilder.DefaultValueFieldFromArrayTemplate(spriteAtlasBase["m_RenderDataMap"]["Array"]);

            // Reuse the GUID from the sprite as the key
            newRenderDataMap["first"]["first"]["data[0]"].AsUInt = uintArray[0];
            newRenderDataMap["first"]["first"]["data[1]"].AsUInt = uintArray[1];
            newRenderDataMap["first"]["first"]["data[2]"].AsUInt = uintArray[2];
            newRenderDataMap["first"]["first"]["data[3]"].AsUInt = uintArray[3];
            newRenderDataMap["first"]["second"].AsLong = 21300000;

            // Texture
            int indexInAtlas = i % 16;
            int x_offset = indexInAtlas % 4 * 512;
            int y_offset = indexInAtlas / 4 * 512;

            newRenderDataMap["second"]["texture"]["m_PathID"].AsLong = atlasIDs[imageDict[pictoName]];
            newRenderDataMap["second"]["textureRect"]["x"].AsFloat = x_offset;
            newRenderDataMap["second"]["textureRect"]["y"].AsFloat = y_offset;
            newRenderDataMap["second"]["textureRect"]["width"].AsFloat = 512;
            newRenderDataMap["second"]["textureRect"]["height"].AsFloat = 512;
            newRenderDataMap["second"]["atlasRectOffset"]["x"].AsFloat = x_offset;
            newRenderDataMap["second"]["atlasRectOffset"]["y"].AsFloat = y_offset;
            newRenderDataMap["second"]["uvTransform"]["x"].AsFloat = 100;
            newRenderDataMap["second"]["uvTransform"]["y"].AsFloat = 256 + x_offset;
            newRenderDataMap["second"]["uvTransform"]["z"].AsFloat = 100;
            newRenderDataMap["second"]["uvTransform"]["w"].AsFloat = 256 + y_offset;
            newRenderDataMap["second"]["downscaleMultiplier"].AsFloat = 1;
            newRenderDataMap["second"]["settingsRaw"].AsUInt = 3;

            spriteAtlasBase["m_RenderDataMap"]["Array"].Children.Add(newRenderDataMap);
        }

        // Apply the new SpriteAtlas
        spriteAtlasInfo.SetNewData(spriteAtlasBase);

        // Remove the template sprite from the bundle
        afile.AssetInfos.Remove(spriteTemplate);

        // Set the new moves in the DanceData/MotionClips/Array
        AssetTypeValueField motionClipsArray = mapBase["DanceData"]["MotionClips"]["Array"];
        AssetTypeValueField goldEffectClipsArray = mapBase["DanceData"]["GoldEffectClips"]["Array"];
        AssetTypeValueField hideHudClips = mapBase["DanceData"]["HideHudClips"]["Array"];
        AssetTypeValueField pictoClips = mapBase["DanceData"]["PictoClips"]["Array"];
        AssetTypeValueField coachCounters = mapBase["HandOnlyCoachDatas"]["Array"];

        motionClipsArray.Children.Clear();
        goldEffectClipsArray.Children.Clear();
        hideHudClips.Children.Clear();
        pictoClips.Children.Clear();
        coachCounters.Children.Clear();

        // For each coach in the song, create a new CoachCounter
        for (int i = 0; i < convert.SongData.CoachCount; i++)
        {
            // Create a new CoachCounter
            AssetTypeValueField newCoachCounter = ValueBuilder.DefaultValueFieldFromArrayTemplate(coachCounters);

            newCoachCounter["GoldMovesCount"].AsUInt = 0;
            newCoachCounter["StandardMovesCount"].AsUInt = 0;

            coachCounters.Children.Add(newCoachCounter);
        }

        foreach (MotionClip clip in convert.SongData.DTape.Clips)
        {
            // If the clip is a GoldEffectClip, add it to the GoldEffectClips array
            switch (clip.__class)
            {
                case "GoldEffectClip":
                    AssetTypeValueField newGoldEffectClip = ValueBuilder.DefaultValueFieldFromArrayTemplate(goldEffectClipsArray);

                    newGoldEffectClip["StartTime"].AsInt = clip.StartTime;
                    newGoldEffectClip["Duration"].AsInt = clip.Duration;
                    newGoldEffectClip["GoldEffectType"].AsInt = clip.EffectType;
                    newGoldEffectClip["Id"].AsLong = clip.Id;
                    newGoldEffectClip["TrackId"].AsLong = clip.TrackId;
                    newGoldEffectClip["IsActive"].AsUInt = (uint)clip.IsActive;

                    goldEffectClipsArray.Children.Add(newGoldEffectClip);
                    break;
                case "PictogramClip":
                    AssetTypeValueField newPictoClip = ValueBuilder.DefaultValueFieldFromArrayTemplate(pictoClips);

                    newPictoClip["StartTime"].AsInt = clip.StartTime;
                    newPictoClip["Duration"].AsInt = clip.Duration;
                    newPictoClip["Id"].AsLong = clip.Id;
                    newPictoClip["TrackId"].AsLong = clip.TrackId;
                    newPictoClip["IsActive"].AsUInt = (uint)clip.IsActive;
                    newPictoClip["PictoPath"].AsString = Path.GetFileNameWithoutExtension(clip.PictoPath);
                    newPictoClip["CoachCount"].AsUInt = (uint)clip.CoachCount;

                    pictoClips.Children.Add(newPictoClip);
                    break;
                case "MotionClip":
                    // Create a new MotionClip
                    AssetTypeValueField newMotionClip = ValueBuilder.DefaultValueFieldFromArrayTemplate(motionClipsArray);

                    string moveName = Path.GetFileNameWithoutExtension(clip.ClassifierPath);

                    newMotionClip["StartTime"].AsInt = clip.StartTime;
                    newMotionClip["Duration"].AsInt = clip.Duration;
                    newMotionClip["Id"].AsLong = clip.Id;
                    newMotionClip["TrackId"].AsLong = clip.TrackId;
                    newMotionClip["IsActive"].AsUInt = (uint)clip.IsActive;
                    newMotionClip["MoveName"].AsString = moveName;
                    newMotionClip["GoldMove"].AsUInt = (uint)clip.GoldMove;
                    newMotionClip["CoachId"].AsInt = clip.CoachId;
                    newMotionClip["MoveType"].AsInt = clip.MoveType;
                    newMotionClip["Color"].AsString = "";

                    // Add the new MotionClip to the array
                    motionClipsArray.Children.Add(newMotionClip);

                    // Increment the coach move counters
                    if (clip.GoldMove == 1)
                        coachCounters.Children[clip.CoachId]["GoldMovesCount"].AsUInt++;
                    else
                        coachCounters.Children[clip.CoachId]["StandardMovesCount"].AsUInt++;

                    break;

                default:
                    Console.WriteLine($"Unknown clip type: {clip.__class}");
                    break;
            }
        }

        // Store all changes
        musicTrackInfos[0].SetNewData(musicTrackBase);
        musicTrackInfos[1].SetNewData(mapBase);

        // Update the preload sizes
        for (int i = 0; i < 2; i++)
        {
            AssetTypeValueField second = assetBundleBase["m_Container"]["Array"][i]["second"];

            // Set equal to the size of the preload array
            second["preloadSize"].AsInt = assetBundleArray.Children.Count;
        }

        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Add .mod to the end of the file
        string outputPackagePath = Path.Combine(convert.OutputFolder, "cachex", "MapPackage");
        bun.SaveAndCompress(outputPackagePath);
    }
}
