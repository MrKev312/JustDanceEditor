using System.Diagnostics;
using System.Text.Json;

using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

using JustDanceEditor.Helpers;

using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Pfim;
using TexturePlugin;
using System;
using System.Text;

namespace JustDanceEditor.JD2Next;

internal class ConvertUbiArtToUnity
{
    public static void Convert()
    {
        // Ask for the mapPackage path
        string mapPackagePath = Question.AskFile("Enter the path to the mapPackage: ", true);
        string originalMapPackagePath = Question.AskFolder("Enter the path to the original map folder (the one containing cache and world): ", true);

        // Get the folder name in /world/maps/*
        string mapsFolder = Path.Combine(originalMapPackagePath, "world", "maps");
        string mapName = Path.GetFileName(Directory.GetDirectories(mapsFolder)[0])!;

        // Get the files in mapsFolder/{mapName}/timeline/moves/wiiu
        string movesFolder = Path.Combine(mapsFolder, mapName, "timeline", "moves", "wiiu");
        string[] moveFiles = Directory.GetFiles(movesFolder);

        // Get the main folder in /cache
        string cacheFolder = Path.Combine(originalMapPackagePath, "cache", "itf_cooked", "nx", "world", "maps", mapName);
        string timelineFolder = Path.Combine(cacheFolder, "timeline");

        // Get the pictos folder in timeline/pictos
        string pictosFolder = Path.Combine(timelineFolder, "pictos");

        // Create a temporary folder
        string tempPictoFolder = Path.Combine(Path.GetTempPath(), "JustDanceEditor", mapName, "pictos");

        // Create the folders
        Directory.CreateDirectory(tempPictoFolder);

        // Before starting on the mapPackage, prepare the pictos
        string[] array = Directory.GetFiles(pictosFolder);
        Console.WriteLine($"Converting {array.Length} pictos...");

        // Get time before starting
        Stopwatch stopwatch = Stopwatch.StartNew();

        Parallel.For(0, array.Length, i =>
        {
            string item = array[i];

            // Stream the file into a new pictos folder, but skip until 0x2C
            using FileStream stream = File.OpenRead(item);
            stream.Seek(0x2C, SeekOrigin.Begin);

            // If the file ends with .png.ckd, change it to .ckd
            if (item.EndsWith(".png.ckd"))
                item = item.Replace(".png.ckd", ".ckd");

            // Get the file name
            string fileName = Path.GetFileNameWithoutExtension(item);

            // Stream the rest of the file into a new file, but with the .xtx extension
            using FileStream newStream = File.Create(Path.Combine(tempPictoFolder, fileName + ".xtx"));
            stream.CopyTo(newStream);

            // Close the streams
            stream.Close();
            newStream.Close();

            // Run xtx_extract on the new file, parameters: -o {filename}.dds {filename}.xtx
            // Print the output to the console
            ProcessStartInfo startInfo = new()
            {
                FileName = "./Resources/xtx_extract.exe",
                Arguments = $"-o \"{Path.Combine(tempPictoFolder, fileName + ".dds")}\" \"{Path.Combine(tempPictoFolder, fileName + ".xtx")}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new() { StartInfo = startInfo })
            {
                process.Start();

                process.WaitForExit();
            }

            // Delete the .xtx file
            File.Delete(Path.Combine(tempPictoFolder, fileName + ".xtx"));

            // Convert the .dds file to .png
            Image<Bgra32> newImage;
            using (IImage image = Pfimage.FromFile(Path.Combine(tempPictoFolder, fileName + ".dds")))
            {

                // If the image is not in Rgba32 format, throw an exception
                if (image.Format != ImageFormat.Rgba32)
                    throw new Exception("Image is not in Rgba32 format!");

                // Create image from image.Data
                newImage = Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height);
            }

            // If the image isn't 512x512, resize it to 512x364
            if (newImage.Width != 512 || newImage.Height != 512)
                newImage.Mutate(x => x.Resize(512, 512));

            //// Place this image in the bottom left corner of a 2048x2048 image
            //Image<Bgra32> newImage2 = new(2048, 2048);
            //newImage2.Mutate(x => x.DrawImage(newImage, new Point(0, 1536), 1));

            // Delete the .dds file
            File.Delete(Path.Combine(tempPictoFolder, fileName + ".dds"));

            // Save the image as a png
            newImage.Save(Path.Combine(tempPictoFolder, fileName + ".png"));
        });

        // Get time after finishing
        stopwatch.Stop();
        Console.WriteLine($"Finished converting pictos in {stopwatch.ElapsedMilliseconds}ms");

        JDNSong originalSong = new()
        {
            // Files end with a null byte, so we remove it
            KTape = JsonSerializer.Deserialize<KaraokeTape>(File.ReadAllText(Path.Combine(timelineFolder, $"{mapName}_tml_karaoke.ktape.ckd")).Replace("\0", ""))!,
            DTape = JsonSerializer.Deserialize<DanceTape>(File.ReadAllText(Path.Combine(timelineFolder, $"{mapName}_tml_dance.dtape.ckd")).Replace("\0", ""))!,
            // pocoloco_musictrack.tpl.ckd
            MTrack = JsonSerializer.Deserialize<MusicTrack>(File.ReadAllText(Path.Combine(cacheFolder, "audio", $"{mapName}_musictrack.tpl.ckd")).Replace("\0", ""))!,
        };
        originalSong.Name = originalSong.DTape.MapName;

        // Open the mapPackage using AssetTools.NET and list the contents
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(mapPackagePath, true);
        AssetBundleFile bun = bunInst.file;
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        // Get the MonoBehaviour that is not named "MusicTrack"
        AssetFileInfo[] musicTrackInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour).ToArray();
        AssetTypeValueField musicTrackBase;
        AssetTypeValueField mapBase;

        // Get the assetbundle
        AssetFileInfo assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundle);
        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        {
            // Set the musicTrackBase to the MonoBehaviour named "MusicTrack"
            AssetTypeValueField temp = manager.GetBaseField(afileInst, musicTrackInfos[0]);

            string name = temp["m_Name"].AsString;

            if (name is "MusicTrack" or "")
            {
                musicTrackBase = temp;
                mapBase = manager.GetBaseField(afileInst, musicTrackInfos[1]);
            }
            else
            {
                musicTrackBase = manager.GetBaseField(afileInst, musicTrackInfos[1]);
                mapBase = temp;

                // Swap the AssetFileInfo's
                (musicTrackInfos[1], musicTrackInfos[0]) = (musicTrackInfos[0], musicTrackInfos[1]);
            }
        }

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

        AssetFileInfo? spriteTemplate = null;

        Queue<uint[]> guids = new();

        // Also remove their corresponding Sprites
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);

            // Extract the GUID from the Sprite
            uint[] guid =
            [
                assetBase["m_RenderDataKey"]["first"]["data[0]"].AsUInt,
                assetBase["m_RenderDataKey"]["first"]["data[1]"].AsUInt,
                assetBase["m_RenderDataKey"]["first"]["data[2]"].AsUInt,
                assetBase["m_RenderDataKey"]["first"]["data[3]"].AsUInt,
            ];

            guids.Enqueue(guid);

            // And remove it from the preload table
            assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == assetInfo.PathId).First());

            // If spriteAtlasId is null, set it to the current sprite's m_SpriteAtlas.m_PathID
            if (spriteTemplate is null)
            {
                spriteTemplate = assetInfo;

                // This one is the template, so we keep it
                continue;
            }

            // Then remove it from the bundle
            afile.AssetInfos.Remove(assetInfo);
        }

        // If the sprite atlas is still null, throw an exception
        if (spriteTemplate is null)
            throw new Exception("Sprite template is null!");

        // Store reference to the SpriteAtlas
        AssetFileInfo spriteAtlasInfo = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.SpriteAtlas).First();
        AssetTypeValueField spriteAtlasBase = manager.GetBaseField(afileInst, spriteAtlasInfo);

        // Empty the packedSprites, packedSpriteNamesToIndex and RenderDataMap arrays
        spriteAtlasBase["m_PackedSprites"]["Array"].Children.Clear();
        spriteAtlasBase["m_PackedSpriteNamesToIndex"]["Array"].Children.Clear();
        spriteAtlasBase["m_RenderDataMap"]["Array"].Children.Clear();

        /// The musicTrackBase:
        // First set the basic fields
        Structure trackStructure = originalSong.MTrack.COMPONENTS[0].trackData.structure;
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
        foreach (KaraokeClip clip in originalSong.KTape.Clips)
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
            karaokeClip["SemitoneTolerance"].AsInt = clip.SemitoneTolerance;
            karaokeClip["StartTimeTolerance"].AsInt = clip.StartTimeTolerance;
            karaokeClip["EndTimeTolerance"].AsInt = clip.EndTimeTolerance;

            // Add the new KaraokeClipContainer to the array
            karaokeArray.Children.Add(newContainer);
        }

        // Empty the moves data
        AssetTypeValueField movesArray = mapBase["HandDeviceMoveModels"]["list"]["Array"];
        movesArray.Children.Clear();

        // Add the new dance moves
        foreach (string item in moveFiles)
        {
            // Get the file name and content, must read as bytes
            string fileName = Path.GetFileName(item);
            byte[] fileContent = File.ReadAllBytes(item);

            // Random new asset id
            long newAssetId = GetRandomId();

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

        // Add the new pictos
        string[] FileDirs = Directory.GetFiles(tempPictoFolder);
        (int width, int height, byte[] endImageBytes)[] bytes = new (int, int, byte[])[FileDirs.Length];

        Parallel.For(0, FileDirs.Length, i =>
        {
            TextureFormat fmt = TextureFormat.DXT5Crunched;
            byte[] platformBlob = [];
            uint platform = afile.Metadata.TargetPlatform;
            int mips = 1;
            string path = FileDirs[i];

            // Load the image
            Image<Rgba32> image = Image.Load<Rgba32>(path);

            byte[] encImageBytes = TextureImportExport.Import(image, fmt, out int width, out int height, ref mips, platform, platformBlob) ?? throw new Exception("Failed to encode image!");

            bytes[i] = (width, height, encImageBytes);
        });

        for (int i = 0; i < FileDirs.Length; i++)
        {
            string item = FileDirs[i];
            // Set each picto as it's own asset
            long textureID = GetRandomId();
            string pictoName = Path.GetFileNameWithoutExtension(item);

            AssetTypeValueField texBaseField = manager.CreateValueBaseField(afileInst, (int)AssetClassID.Texture2D);
            (int width, int height, byte[] endImageBytes) = bytes[i];

            // Set the name and content
            texBaseField["m_Name"].AsString = $"sactx-{i}-{width}x{height}-Crunch-Origin-5e98ca96";
            //texBaseField["m_Name"].AsString = pictoName;
            texBaseField["m_MipCount"].AsInt = 1;

            AssetTypeValueField m_StreamData = texBaseField["m_StreamData"];
            m_StreamData["offset"].AsInt = 0;
            m_StreamData["size"].AsInt = 0;
            m_StreamData["path"].AsString = "";

            texBaseField["m_ForcedFallbackFormat"].AsInt = (int)TextureFormat.RGBA32;
            texBaseField["m_TextureFormat"].AsInt = (int)TextureFormat.DXT5Crunched;
            // todo: size for multi image textures
            texBaseField["m_CompleteImageSize"].AsUInt = (uint)endImageBytes.Length;
            texBaseField["m_ImageCount"].AsInt = 1;
            texBaseField["m_TextureDimension"].AsInt = 2;

            texBaseField["m_TextureSettings"]["m_FilterMode"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_Aniso"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapU"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapV"].AsInt = 1;
            texBaseField["m_TextureSettings"]["m_WrapW"].AsInt = 1;

            texBaseField["m_ColorSpace"].AsInt = 1;

            texBaseField["m_Width"].AsInt = width;
            texBaseField["m_Height"].AsInt = height;

            AssetTypeValueField image_data = texBaseField["image data"];
            image_data.Value.ValueType = AssetValueType.ByteArray;
            image_data.TemplateField.ValueType = AssetValueType.ByteArray;
            image_data.AsByteArray = endImageBytes;

            // Make a new AssetFileInfo
            AssetFileInfo newInfo = AssetFileInfo.Create(afile, textureID, (int)AssetClassID.Texture2D, null);
            newInfo.SetNewData(texBaseField);

            // Add the new AssetFileInfo to the AssetsFile
            afile.Metadata.AddAssetInfo(newInfo);

            // Add the new move reference to the preload table
            AssetTypeValueField newAssetBundle = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newAssetBundle["m_PathID"].AsLong = textureID;
            assetBundleArray.Children.Add(newAssetBundle);

            // Now we create a new Sprite
            long spriteID = GetRandomId();

            // Load in the sprite template
            AssetTypeValueField spriteBaseField = manager.GetBaseField(afileInst, spriteTemplate);

            // Set the name and content
            spriteBaseField["m_Name"].AsString = pictoName;
            spriteBaseField["m_Rect"]["width"].AsFloat = 512;
            spriteBaseField["m_Rect"]["height"].AsFloat = 512;
            spriteBaseField["m_RD"]["textureRect"]["width"].AsFloat = 512;
            spriteBaseField["m_RD"]["textureRect"]["height"].AsFloat = 512;

            uint[] uintArray = GUID();

            // Use the GUID for the texture as the key
            spriteBaseField["m_RenderDataKey"]["first"]["data[0]"].AsUInt = uintArray[0];
            spriteBaseField["m_RenderDataKey"]["first"]["data[1]"].AsUInt = uintArray[1];
            spriteBaseField["m_RenderDataKey"]["first"]["data[2]"].AsUInt = uintArray[2];
            spriteBaseField["m_RenderDataKey"]["first"]["data[3]"].AsUInt = uintArray[3];
            spriteBaseField["m_RenderDataKey"]["second"].AsLong = 21300000;

            // Add the new Sprite to the AssetsFile
            AssetFileInfo newSpriteInfo = AssetFileInfo.Create(afile, spriteID, (int)AssetClassID.Sprite, null);
            newSpriteInfo.SetNewData(spriteBaseField);

            // Add the new AssetFileInfo to the AssetsFile
            afile.Metadata.AddAssetInfo(newSpriteInfo);

            // Add the new move reference to the preload table
            newAssetBundle = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
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

            // Texture
            newRenderDataMap["second"]["texture"]["m_PathID"].AsLong = textureID;
            newRenderDataMap["second"]["textureRect"]["width"].AsFloat = 512;
            newRenderDataMap["second"]["textureRect"]["height"].AsFloat = 512;
            newRenderDataMap["second"]["uvTransform"]["x"].AsFloat = 100;
            newRenderDataMap["second"]["uvTransform"]["y"].AsFloat = 256;
            newRenderDataMap["second"]["uvTransform"]["z"].AsFloat = 100;
            newRenderDataMap["second"]["uvTransform"]["w"].AsFloat = 256;
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

        motionClipsArray.Children.Clear();
        goldEffectClipsArray.Children.Clear();
        hideHudClips.Children.Clear();
        pictoClips.Children.Clear();

        foreach (MotionClip clip in originalSong.DTape.Clips)
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
                    break;

                default:
                    Console.WriteLine($"Unknown clip type: {clip.__class}");
                    break;
            }
        }

        // Set the counts
        mapBase["HandOnlyCoachDatas"]["Array"][0]["GoldMovesCount"].AsUInt = (uint)goldEffectClipsArray.Children.Count;
        mapBase["HandOnlyCoachDatas"]["Array"][0]["StandardMovesCount"].AsUInt = (uint)motionClipsArray.Children.Count;

        // Store all changes
        musicTrackInfos[0].SetNewData(musicTrackBase);
        musicTrackInfos[1].SetNewData(mapBase);
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Add .mod to the end of the file
        string uncompressedPath = Path.ChangeExtension(mapPackagePath, ".mod.uncompressed");
        string compressedPath = Path.ChangeExtension(mapPackagePath, ".mod");

        using (AssetsFileWriter writer = new(uncompressedPath))
            bun.Write(writer);

        AssetBundleFile newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(File.OpenRead(uncompressedPath)));

        using (AssetsFileWriter compressedWriter = new(compressedPath))
        {
            newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);
        }

        newUncompressedBundle.Close();

        // Function to get a random ID that is not used yet
        long GetRandomId()
        {
            long id = Random.Shared.NextInt64(long.MinValue, long.MaxValue);

            while (afile.Metadata.GetAssetInfo(id) is not null)
                id = Random.Shared.NextInt64(long.MinValue, long.MaxValue);

            return id;
        }

        uint[] GUID()
        {
            //byte[] guidBytes = Guid.NewGuid().ToByteArray();
            //uint[] uintArray = new uint[4];

            //for (int j = 0; j < guidBytes.Length; j++)
            //	guidBytes[j] = (byte)(((guidBytes[j] & 0xF0) >> 4) | ((guidBytes[j] & 0x0F) << 4));

            //for (int j = 0; j < 4; j++)
            //	uintArray[j] = BitConverter.ToUInt32(guidBytes, j * 4);

            //return uintArray;

            return guids.Dequeue();
        }
    }
}
