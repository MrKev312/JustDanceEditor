using System.Diagnostics;
using System.Text.Json;

using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Pfim;
using HtmlAgilityPack;
using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.Resources;
using JustDanceEditor.Converter.UbiArt.Tapes;
using JustDanceEditor.Converter.Unity.TextureConverter;
using JustDanceEditor.Converter.UbiArt;

namespace JustDanceEditor.Converter.Converters;

public class ConvertUbiArtToUnity
{
    public static void Convert(ConversionRequest conversionRequest)
    {
        Console.WriteLine();

        // Check if there's a template folder
        if (!Directory.Exists(conversionRequest.TemplatePath))
        {
            throw new FileNotFoundException("Template folder not found");
        }

        /// TODO: move this to the front end and have this function take these as parameters
        // Get the path to the mapPackage in /template/cachex/MapPackage/*, only one file should be in there
        //string mapPackagePath = Directory.GetFiles(Path.Combine("./template", "cachex", "MapPackage"))[0];
        //string originalMapPackagePath = Question.AskFolder("Enter the path to the map folder you want to convert (the one containing cache and world): ", true);
        //string outputFolder = Question.AskFolder("Enter the path to the output folder: ", true);
        string mapPackagePath = conversionRequest.TemplatePath;
        string originalMapPackagePath = conversionRequest.InputPath;
        string outputFolder = conversionRequest.OutputPath;

        /// Get all the imporant folders
        // Get the folder name in /world/maps/*
        string mapsFolder = Path.Combine(originalMapPackagePath, "world", "maps");
        string mapName = Path.GetFileName(Directory.GetDirectories(mapsFolder)[0])!;

        outputFolder = Path.Combine(outputFolder, mapName);

        // Use the shared random
        Random rand = Random.Shared;

        // Get the files in mapsFolder/{mapName}/timeline/moves/wiiu
        string movesFolder = Path.Combine(mapsFolder, mapName, "timeline", "moves", "wiiu");
        string[] moveFiles = Directory.GetFiles(movesFolder);

        // Get the main folder in /cache
        string cacheFolder = Path.Combine(originalMapPackagePath, "cache", "itf_cooked", "nx", "world", "maps", mapName);
        string timelineFolder = Path.Combine(cacheFolder, "timeline");

        // Get the pictos folder in timeline/pictos
        string pictosFolder = Path.Combine(timelineFolder, "pictos");

        /// Load in the song data
        // Get the song info
        Console.WriteLine("Loading song info...");

        JDUbiArtSong originalSong = new()
        {
            // Files end with a null byte, so we remove it
            KTape = JsonSerializer.Deserialize<KaraokeTape>(File.ReadAllText(Path.Combine(timelineFolder, $"{mapName}_tml_karaoke.ktape.ckd")).Replace("\0", ""))!,
            DTape = JsonSerializer.Deserialize<DanceTape>(File.ReadAllText(Path.Combine(timelineFolder, $"{mapName}_tml_dance.dtape.ckd")).Replace("\0", ""))!,
            // pocoloco_musictrack.tpl.ckd
            MTrack = JsonSerializer.Deserialize<MusicTrack>(File.ReadAllText(Path.Combine(cacheFolder, "audio", $"{mapName}_musictrack.tpl.ckd")).Replace("\0", ""))!,
            SongDesc = JsonSerializer.Deserialize<SongDesc>(File.ReadAllText(Path.Combine(cacheFolder, "songdesc.tpl.ckd")).Replace("\0", ""))!
        };
        originalSong.Name = originalSong.DTape.MapName;
        mapName = originalSong.Name;

        /// Start converting
        // Create a temporary folder
        string tempMapFolder = Path.Combine(Path.GetTempPath(), "JustDanceEditor", mapName);
        string tempPictoFolder = Path.Combine(tempMapFolder, "pictos");

        // Create the folders
        Directory.CreateDirectory(tempPictoFolder);

        // Before starting on the mapPackage, prepare the pictos
        string[] pictoImageFiles = Directory.GetFiles(pictosFolder);
        Console.WriteLine($"Converting {pictoImageFiles.Length} pictos...");

        // Get time before starting
        Stopwatch stopwatch = Stopwatch.StartNew();

        Parallel.For(0, pictoImageFiles.Length, i =>
        {
            string item = pictoImageFiles[i];

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
                RedirectStandardOutput = false,
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

            // Delete the .dds file
            File.Delete(Path.Combine(tempPictoFolder, fileName + ".dds"));

            // Save the image as a png
            newImage.Save(Path.Combine(tempPictoFolder, fileName + ".png"));
        });

        Dictionary<string, int> imageDict = [];
        List<Image<Rgba32>> atlasPics = [];
        Image<Rgba32>? atlasImage = null;

        Console.WriteLine("Creating atlasses...");

        // Get the files in the pictos folder
        string[] pictoFiles = Directory.GetFiles(tempPictoFolder);

        // Convert the 512x512 images to a 2048x2048 atlas
        // Use 4 pixels of padding between each image
        for (int i = 0; i < pictoFiles.Length; i++)
        {
            int indexInAtlas = i % 16;

            if (indexInAtlas == 0 || atlasImage is null)
                // Create a new image
                atlasImage = new(2048, 2048);

            // Get the current image
            (Image<Rgba32> image, string name) = (Image.Load<Rgba32>(pictoFiles[i]), Path.GetFileNameWithoutExtension(pictoFiles[i]));

            // Get the x and y coordinates
            int x_coord = indexInAtlas % 4 * 512;
            int y_coord = indexInAtlas / 4 * 512;

            // Because the y is calculated from the top left corner, we need to subtract it from 2048
            y_coord = 2048 - y_coord - 512;

            // Draw the image on the atlas
            atlasImage.Mutate(x => x.DrawImage(image, new Point(x_coord, y_coord), 1));

            // Dispose the image
            image.Dispose();

            // Store the name and the atlas index in the dictionary
            imageDict.Add(name, i / 16);

            // If this is the last image, or i % 16 == 15, add the image to the atlasPics array
            if (indexInAtlas == 15 || i == pictoFiles.Length - 1)
            {
                // Add the image to the atlasPics array
                atlasPics.Add(atlasImage);

                // Set to null but don't dispose
                atlasImage = null;
            }
        }

        // Save the atlasPics in the tempPictoFolder in the format atlas_{index}.png
        Directory.CreateDirectory(Path.Combine(tempPictoFolder, "Atlas"));

        for (int i = 0; i < atlasPics.Count; i++)
            atlasPics[i].Save(Path.Combine(tempPictoFolder, "Atlas", $"atlas_{i}.png"));

        // Get time after finishing
        stopwatch.Stop();
        Console.WriteLine($"Finished converting pictos in {stopwatch.ElapsedMilliseconds}ms");

        // Convert the menu art in \cache\itf_cooked\nx\world\maps\pocoloco\menuart\textures
        string menuArtFolder = Path.Combine(cacheFolder, "menuart", "textures");
        string tempMenuArtFolder = Path.Combine(Path.GetTempPath(), "JustDanceEditor", mapName, "menuart");
        string[] menuArtFiles = Directory.GetFiles(menuArtFolder);

        // Create the folders
        Directory.CreateDirectory(tempMenuArtFolder);

        // Get time before starting
        stopwatch.Restart();

        Console.WriteLine($"Converting {menuArtFiles.Length} menu art files...");

        Parallel.For(0, menuArtFiles.Length, i =>
        {
            string item = menuArtFiles[i];

            // Stream the file into a new menuart folder, but skip until 0x2C
            using FileStream stream = File.OpenRead(item);
            stream.Seek(0x2C, SeekOrigin.Begin);

            // Get the file name
            string fileName = Path.GetFileNameWithoutExtension(item);

            // Stream the rest of the file into a new file, but with the .xtx extension
            using FileStream newStream = File.Create(Path.Combine(tempMenuArtFolder, fileName + ".xtx"));
            stream.CopyTo(newStream);

            // Close the streams
            stream.Close();
            newStream.Close();

            // Run xtx_extract on the new file, parameters: -o {filename}.dds {filename}.xtx
            // Print the output to the console
            ProcessStartInfo startInfo = new()
            {
                FileName = "./Resources/xtx_extract.exe",
                Arguments = $"-o \"{Path.Combine(tempMenuArtFolder, fileName + ".dds")}\" \"{Path.Combine(tempMenuArtFolder, fileName + ".xtx")}\"",
                RedirectStandardOutput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new() { StartInfo = startInfo })
            {
                process.Start();

                process.WaitForExit();
            }

            // Delete the .xtx file
            File.Delete(Path.Combine(tempMenuArtFolder, fileName + ".xtx"));

            // Convert the .dds file to .png
            Image<Bgra32> newImage;
            using (IImage image = Pfimage.FromFile(Path.Combine(tempMenuArtFolder, fileName + ".dds")))
            {

                // If the image is not in Rgba32 format, throw an exception
                if (image.Format != ImageFormat.Rgba32)
                    throw new Exception("Image is not in Rgba32 format!");

                // Create image from image.Data
                newImage = Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height);
            }

            newImage.Save(Path.Combine(tempMenuArtFolder, fileName + ".png"));

            // Delete the .dds file
            File.Delete(Path.Combine(tempMenuArtFolder, fileName + ".dds"));
        });

        // Get time after finishing
        stopwatch.Stop();
        Console.WriteLine($"Finished converting menu art files in {stopwatch.ElapsedMilliseconds}ms");

        // Convert the audio files in /cache/itf_cooked/nx/world/maps/{mapName}/audio
        Console.WriteLine("Converting audio files...");
        stopwatch.Restart();

        string tempAudioFolder = Path.Combine(tempMapFolder, "audio");
        Directory.CreateDirectory(tempAudioFolder);

        // Get the mainsequence tape
        string mainSequenceTapePath = Path.Combine(cacheFolder, "cinematics", $"{mapName}_mainsequence.tape.ckd");
        MainSequence mainSequence = JsonSerializer.Deserialize<MainSequence>(File.ReadAllText(mainSequenceTapePath).Replace("\0", ""))!;

        // Get the audio files
        // Foreach where __class is SoundSetClip
        AudioVibrationClip[] audioClips = mainSequence.Clips.Where(s => s.__class == "SoundSetClip").ToArray();
        foreach (AudioVibrationClip audioVibrationClip in audioClips)
        {
            string fileName = Path.GetFileNameWithoutExtension(audioVibrationClip.SoundSetPath);

            // In audio/ambs folder, convert the .wav.ckd file to .wav
            string wavPath = Path.Combine(cacheFolder, "audio", "amb", $"{fileName}.wav.ckd");
            string newWavPath = Path.Combine(tempAudioFolder, $"{fileName}.wav");

            // Run vgmstream on the .wav.ckd file
            VGMStream.Convert(wavPath, newWavPath).Wait();
        }

        // Now convert the main song
        string mainSongPath;

        // If the world\maps\{mapName}\audio\ folder exists, use the file in there
        if (Directory.Exists(Path.Combine(mapsFolder, mapName, "audio")))
            mainSongPath = Directory.GetFiles(Path.Combine(mapsFolder, mapName, "audio"))[0];
        else
            // Else use the file in cache\itf_cooked\nx\world\maps\{mapName}\audio that ends with .wav.ckd
            mainSongPath = Directory.GetFiles(Path.Combine(cacheFolder, "audio")).Where(x => x.EndsWith(".wav.ckd")).First()!;

        string newMainSongPath = Path.Combine(tempAudioFolder, "mainSong.wav");

        // Run vgmstream on the main song
        VGMStream.Convert(mainSongPath, newMainSongPath).Wait();

        // Get time after finishing
        stopwatch.Stop();
        Console.WriteLine($"Finished converting audio files in {stopwatch.ElapsedMilliseconds}ms");

        // Now we merge them all together according to their offset
        Console.WriteLine("Merging audio files...");
        stopwatch.Restart();

        double divisionRaw = originalSong.MTrack.COMPONENTS[0].trackData.structure.avgMarkerDistance / 48d;
        double division = 2502.66305525460462d / (6000d / divisionRaw);

        // Get the lowest offset from the audioClips
        // Everything will be offset by this value such that the lowest offset is 0, aka the start of the audiofile
        int lowestOffset = audioClips.Min(x => x.StartTime);

        (string path, float offset)[] audioFiles = new (string path, float offset)[audioClips.Length + 1];
        for (int i = 0; i < audioClips.Length; i++)
        {
            AudioVibrationClip audioVibrationClip = audioClips[i];
            string fileName = Path.GetFileNameWithoutExtension(audioVibrationClip.SoundSetPath);
            string wavPath = Path.Combine(tempAudioFolder, $"{fileName}.wav");

            // Get the offset
            float offset = (float)((audioVibrationClip.StartTime - lowestOffset) / division);

            // Add the file to the array
            audioFiles[i] = (wavPath, offset);
        }

        // Add the main song to the end of the array
        audioFiles[^1] = (newMainSongPath, (float)((0 - lowestOffset) / division));

        // Merge the audio files
        Audio.MergeAudioFiles(audioFiles, Path.Combine(tempAudioFolder, "merged.wav"));

        #region MapPackage
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
        assetBundleBase["m_Name"].AsString = $"{mapName}_MapPackage";
        assetBundleBase["m_AssetBundleName"].AsString = $"{mapName}_MapPackage";

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

        // Set the name of the mapBase to the name of the map
        mapBase["m_Name"].AsString = mapName;
        mapBase["MapName"].AsString = mapName;
        mapBase["SongDesc"]["MapName"].AsString = mapName;
        mapBase["KaraokeData"]["MapName"].AsString = mapName;
        mapBase["DanceData"]["MapName"].AsString = mapName;

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
        spriteAtlasBase["m_Name"].AsString = mapName;
        spriteAtlasBase["m_Tag"].AsString = mapName;

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
        string[] FileDirs = Directory.GetFiles(Path.Combine(tempPictoFolder, "Atlas"));
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
            long newAssetId = GetRandomId();

            // Create a new AssetTypeValueField
            AssetTypeValueField texBaseField = manager.CreateValueBaseField(afileInst, (int)AssetClassID.Texture2D);

            // Set the name and content
            texBaseField["m_Name"].AsString = $"sactx-{i}-{2048}x{2048}-Crunch-{mapName}-5e98ca96";
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
        FileDirs = Directory.GetFiles(tempPictoFolder);

        for (int i = 0; i < FileDirs.Length; i++)
        {
            string item = FileDirs[i];

            // Set each picto as it's own asset
            string pictoName = Path.GetFileNameWithoutExtension(item);

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
            spriteBaseField["m_AtlasTags"]["Array"].Children[0].AsString = mapName;

            uint[] uintArray = GUID();

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
        // I have no idea why this isn't just the length of the array, but seems to always be around half of it
        mapBase["HandOnlyCoachDatas"]["Array"][0]["StandardMovesCount"].AsUInt = (uint)motionClipsArray.Children.Count / 2;

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
        string outputPackagePath = Path.Combine(outputFolder, "cachex", "MapPackage");
        Directory.CreateDirectory(outputPackagePath);
        string uncompressedPath = Path.Combine(outputPackagePath, "temp.mod.uncompressed");
        string compressedPath = Path.Combine(outputPackagePath, "temp.mod");

        using (AssetsFileWriter writer = new(uncompressedPath))
            bun.Write(writer);

        AssetBundleFile newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(File.OpenRead(uncompressedPath)));

        using (AssetsFileWriter compressedWriter = new(compressedPath))
            newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);

        newUncompressedBundle.Close();

        // Delete the uncompressed file
        File.Delete(uncompressedPath);

        // Rename the compressed file to it's md5 hash
        string hash = Download.GetFileMD5(compressedPath);
        string newPath = Path.Combine(outputPackagePath, $"{hash}");
        File.Move(compressedPath, newPath);

        #endregion

        #region Coaches

        // Get the coaches folder
        // /template/cachex/CoachesLarge/*
        string coacheLargePackagePath = Directory.GetFiles(Path.Combine("./template", "cachex", "CoachesLarge"))[0];

        Console.WriteLine("Converting CoachesLarge...");

        // Open the coaches package using AssetTools.NET
        manager = new();
        bunInst = manager.LoadBundleFile(coacheLargePackagePath, true);
        bun = bunInst.file;
        afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        afile = afileInst.file;
        afile.GenerateQuickLookup();

        sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        assetBundleBase = manager.GetBaseField(afileInst, assetBundle);
        assetBundleBase["m_Name"].AsString = $"{mapName}_CoachesLarge";
        assetBundleBase["m_AssetBundleName"].AsString = $"{mapName}_CoachesLarge";
        assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        // Get all texture2d's
        AssetFileInfo[] textureInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D).ToArray();
        AssetFileInfo[] spriteInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite).ToArray();

        foreach (AssetFileInfo coachInfo in textureInfos)
        {
            AssetTypeValueField coachBase = manager.GetBaseField(afileInst, coachInfo);

            // Get the name
            string coachName = coachBase["m_Name"].AsString;

            byte[] encImageBytes;
            TextureFormat fmt = TextureFormat.DXT5Crunched;
            byte[] platformBlob = [];
            uint platform = afile.Metadata.TargetPlatform;
            int mips = 1;
            string path = Path.Combine(tempMenuArtFolder, $"{mapName}_map_bkg.tga.png");
            Image<Rgba32> image;
            int width, height;

            // If the name ends in bkg
            if (coachName.EndsWith("bkg"))
            {
                // Set the name to {mapName}_bkg
                coachBase["m_Name"].AsString = $"{mapName}_map_bkg";

                fmt = TextureFormat.DXT1Crunched;
                path = Path.Combine(tempMenuArtFolder, $"{mapName}_map_bkg.tga.png");

                // Load the image
                image = Image.Load<Rgba32>(path);

                encImageBytes = TextureImportExport.Import(image, fmt, out width, out height, ref mips, platform, platformBlob) ?? throw new Exception("Failed to encode image!");

                // Set the image data
                coachBase["image data"].AsByteArray = encImageBytes;
                coachBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;

                // Save the file
                coachInfo.SetNewData(coachBase);

                continue;
            }

            // If the name does not end with 1, delete it
            if (!coachName.EndsWith('1'))
            {
                // Remove the coach from the bundle
                afile.AssetInfos.Remove(coachInfo);

                // And remove it from the preload table
                assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == coachInfo.PathId).First());

                continue;
            }

            path = Path.Combine(tempMenuArtFolder, $"{mapName}_Coach_1.tga.png");

            // Load the image
            image = Image.Load<Rgba32>(path);

            encImageBytes = TextureImportExport.Import(image, fmt, out width, out height, ref mips, platform, platformBlob) ?? throw new Exception("Failed to encode image!");

            // Set the name to {mapName}_coach
            coachBase["m_Name"].AsString = $"{mapName}_Coach_1";

            // Set the image data
            coachBase["image data"].AsByteArray = encImageBytes;
            coachBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;

            // Save the file
            coachInfo.SetNewData(coachBase);
        }

        // For each sprite in the file
        foreach (AssetFileInfo coachInfo in spriteInfos)
        {
            AssetTypeValueField coachBase = manager.GetBaseField(afileInst, coachInfo);

            // Get the name
            string coachName = coachBase["m_Name"].AsString;

            // If the name ends in bkg
            if (coachName.EndsWith("bkg"))
            {
                // Set the name to {mapName}_bkg
                coachBase["m_Name"].AsString = $"{mapName}_map_bkg";

                // Save the file
                coachInfo.SetNewData(coachBase);

                continue;
            }

            // If the name does not end with 1, delete it
            if (!coachName.EndsWith('1'))
            {
                // Remove the coach from the bundle
                afile.AssetInfos.Remove(coachInfo);

                // And remove it from the preload table
                assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == coachInfo.PathId).First());

                continue;
            }

            // Set the name to {mapName}_coach
            coachBase["m_Name"].AsString = $"{mapName}_Coach_1";

            // Save the file
            coachInfo.SetNewData(coachBase);
        }

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Add .mod to the end of the file
        outputPackagePath = Path.Combine(outputFolder, "cachex", "CoachesLarge");
        Directory.CreateDirectory(outputPackagePath);
        uncompressedPath = Path.Combine(outputPackagePath, "temp.mod.uncompressed");
        compressedPath = Path.Combine(outputPackagePath, "temp.mod");

        using (AssetsFileWriter writer = new(uncompressedPath))
            bun.Write(writer);

        newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(File.OpenRead(uncompressedPath)));

        using (AssetsFileWriter compressedWriter = new(compressedPath))
            newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);

        newUncompressedBundle.Close();

        // Delete the uncompressed file
        File.Delete(uncompressedPath);

        // Rename the compressed file to it's md5 hash
        hash = Download.GetFileMD5(compressedPath);
        newPath = Path.Combine(outputPackagePath, $"{hash}");
        File.Move(compressedPath, newPath);

        // Now we do the same for the coaches small
        string coacheSmallPackagePath = Directory.GetFiles(Path.Combine("./template", "cachex", "CoachesSmall"))[0];

        Console.WriteLine("Converting CoachesSmall...");

        // Open the coaches package using AssetTools.NET
        manager = new();
        bunInst = manager.LoadBundleFile(coacheSmallPackagePath, true);
        bun = bunInst.file;
        afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        afile = afileInst.file;
        afile.GenerateQuickLookup();

        sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        assetBundleBase = manager.GetBaseField(afileInst, assetBundle);
        assetBundleBase["m_Name"].AsString = $"{mapName}_CoachesSmall";
        assetBundleBase["m_AssetBundleName"].AsString = $"{mapName}_CoachesSmall";
        assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        // Get all texture2d's
        textureInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D).ToArray();
        spriteInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite).ToArray();

        foreach (AssetFileInfo coachInfo in textureInfos)
        {
            AssetTypeValueField coachBase = manager.GetBaseField(afileInst, coachInfo);

            // Get the name
            string coachName = coachBase["m_Name"].AsString;

            byte[] encImageBytes;
            TextureFormat fmt = TextureFormat.DXT5Crunched;
            byte[] platformBlob = [];
            uint platform = afile.Metadata.TargetPlatform;
            int mips = 1;
            string path = Path.Combine(tempMenuArtFolder, $"{mapName}_map_bkg.tga.png");
            Image<Rgba32> image;
            int width, height;

            // If the name does not end with 1_Phone, delete it
            if (!coachName.EndsWith("1_Phone"))
            {
                // Remove the coach from the bundle
                afile.AssetInfos.Remove(coachInfo);

                // And remove it from the preload table
                assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == coachInfo.PathId).First());

                continue;
            }

            path = Path.Combine(tempMenuArtFolder, $"{mapName}_map_bkg.tga.png");

            // Load the image
            image = Image.Load<Rgba32>(path);

            // Resize the image to 256x256
            image.Mutate(x => x.Resize(256, 256));

            encImageBytes = TextureImportExport.Import(image, fmt, out width, out height, ref mips, platform, platformBlob) ?? throw new Exception("Failed to encode");

            // Set the name to {mapName}_Coach_1_Phone
            coachBase["m_Name"].AsString = $"{mapName}_Coach_1_Phone";

            // Set the image data
            coachBase["image data"].AsByteArray = encImageBytes;
            coachBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
            coachBase["m_StreamData"]["offset"].AsInt = 0;
            coachBase["m_StreamData"]["size"].AsInt = 0;
            coachBase["m_StreamData"]["path"].AsString = "";

            // Save the file
            coachInfo.SetNewData(coachBase);
        }

        // For each sprite in the file
        foreach (AssetFileInfo coachInfo in spriteInfos)
        {
            AssetTypeValueField coachBase = manager.GetBaseField(afileInst, coachInfo);

            // Get the name
            string coachName = coachBase["m_Name"].AsString;

            // If the name does not end with 1_Phone, delete it
            if (!coachName.EndsWith("1_Phone"))
            {
                // Remove the coach from the bundle
                afile.AssetInfos.Remove(coachInfo);

                // And remove it from the preload table
                assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == coachInfo.PathId).First());

                continue;
            }

            // Set the name to {mapName}_Coach_1_Phone
            coachBase["m_Name"].AsString = $"{mapName}_Coach_1_Phone";

            // Save the file
            coachInfo.SetNewData(coachBase);
        }

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Add .mod to the end of the file
        outputPackagePath = Path.Combine(outputFolder, "cachex", "CoachesSmall");
        Directory.CreateDirectory(outputPackagePath);
        uncompressedPath = Path.Combine(outputPackagePath, "temp.mod.uncompressed");
        compressedPath = Path.Combine(outputPackagePath, "temp.mod");

        using (AssetsFileWriter writer = new(uncompressedPath))
            bun.Write(writer);

        newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(File.OpenRead(uncompressedPath)));

        using (AssetsFileWriter compressedWriter = new(compressedPath))
            newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);

        newUncompressedBundle.Close();

        // Delete the uncompressed file
        File.Delete(uncompressedPath);

        // Rename the compressed file to it's md5 hash
        hash = Download.GetFileMD5(compressedPath);
        newPath = Path.Combine(outputPackagePath, $"{hash}");
        File.Move(compressedPath, newPath);
        #endregion

        #region Cover
        // Now we basically do the same for the cover
        string coverPackagePath = Directory.GetFiles(Path.Combine("./template", "cache0", "Cover"))[0];

        Console.WriteLine("Converting Cover...");

        // Open the coaches package using AssetTools.NET
        manager = new();
        bunInst = manager.LoadBundleFile(coverPackagePath, true);
        bun = bunInst.file;
        afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        afile = afileInst.file;
        afile.GenerateQuickLookup();

        sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        assetBundleBase = manager.GetBaseField(afileInst, assetBundle);
        assetBundleBase["m_Name"].AsString = $"{mapName}_Cover";
        assetBundleBase["m_AssetBundleName"].AsString = $"{mapName}_Cover";
        assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        // There's only one texture2d in the cover, so we can just get it
        AssetFileInfo coverInfo = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D).First();
        AssetTypeValueField coverBase = manager.GetBaseField(afileInst, coverInfo);

        // Set the name to {mapName}_Cover_2x
        coverBase["m_Name"].AsString = $"{mapName}_Cover_2x";

        Image<Rgba32>? coverImage = null;

        // If a cover.png exists in the map folder, use that
        if (File.Exists(Path.Combine(originalMapPackagePath, "cover.png")))
        {
            coverImage = Image.Load<Rgba32>(Path.Combine(originalMapPackagePath, "cover.png"));

            // Stretch it to 640x360, this shouldn't do anything to correctly sized images
            coverImage.Mutate(x => x.Resize(640, 360));
        }
        else
        {
            // Download one from https://justdance.fandom.com/wiki/User_blog:Sweet_King_Candy/Extended_Covers_for_Just_Dance_%2B
            {
                // Load the webpage
                HttpClient client = new();
                string html = client.GetStringAsync("https://justdance.fandom.com/wiki/User_blog:Sweet_King_Candy/Extended_Covers_for_Just_Dance_%2B").Result;

                // Convert the html to a document
                HtmlDocument doc = new();
                doc.LoadHtml(html);

                // Get the class called "fandom-table"[0]/tbody
                HtmlNode table = doc.DocumentNode.SelectNodes("//table[@class='fandom-table']")[0];
                HtmlNode htmlNode = table.SelectSingleNode("tbody");

                // Now get the node where the first td is the map name
                List<HtmlNode> rows = htmlNode.SelectNodes("tr").Skip(1).Where(x => x.SelectSingleNode("td").InnerText == originalSong.SongDesc.COMPONENTS[0].Title).ToList();
                // Select the first one, if it exists, if not, null
                HtmlNode? row = rows.Count > 0 ? rows[0] : null;

                // If the row is null, then the cover doesn't exist
                bool coverExists = row is not null;

                if (coverExists)
                // If the cover exists, then we can just download it
                {
                    // If both the last or second to last td's are empty or "N/A", then the cover doesn't exist
                    HtmlNodeCollection tds = row.SelectNodes("td");
                    string coverUrl = "";

                    // Get the cover url
                    if (tds[^1].InnerText.Contains("PlaceHolderCover2023") || tds[^1].InnerText == "")
                        coverUrl = tds[^1].SelectSingleNode("a").Attributes["href"].Value;
                    else if (tds[^2].InnerText.Contains("PlaceHolderCover2023") || tds[^2].InnerText == "")
                        coverUrl = tds[^2].SelectSingleNode("a").Attributes["href"].Value;
                    else
                        coverExists = false;

                    if (coverExists)
                    {
                        Stream coverStream = client.GetStreamAsync(coverUrl).Result;

                        // Load the image
                        coverImage = Image.Load<Rgba32>(coverStream);
                        coverImage.Mutate(x => x.Resize(640, 360));
                    }
                }

                if (!coverExists)
                // Manually create the cover
                {
                    // Now we gotta make a custom texture from the scraps we have in the menu art folder
                    // First we load in the background
                    string backgroundPath = Path.Combine(tempMenuArtFolder, $"{mapName}_map_bkg.tga.png");
                    coverImage = Image.Load<Rgba32>(backgroundPath);

                    // Stretch it to 2048x1024, this shouldn't do anything to correctly sized images
                    coverImage.Mutate(x => x.Resize(2048, 1024));

                    // Then we load in the albumcoach
                    string albumCoachPath = Path.Combine(tempMenuArtFolder, $"{mapName}_cover_albumcoach.tga.png");
                    Image<Rgba32> albumCoach = Image.Load<Rgba32>(albumCoachPath);

                    // Then we place the albumcoach on top of the background in the center
                    // The background is 2048x1024 and the albumcoach is 1024x1024
                    // So we place it at 512, 0
                    coverImage.Mutate(x => x.DrawImage(albumCoach, new Point(512, 0), 1));

                    // Now we resize the image down to 720x360
                    coverImage.Mutate(x => x.Resize(720, 360));

                    // Now we crop the image to 640x360 centered
                    coverImage.Mutate(x => x.Crop(new Rectangle(40, 0, 640, 360)));
                }
            }
        }

        // Save the image in the temp folder
        coverImage!.Save(Path.Combine(tempMenuArtFolder, $"Cover_{mapName}.png"));

        // Now we can encode the image
        {
            byte[] encImageBytes;
            TextureFormat fmt = TextureFormat.DXT1Crunched;
            byte[] platformBlob = [];
            uint platform = afile.Metadata.TargetPlatform;
            int mips = 1;
            string path = Path.Combine(tempMenuArtFolder, $"{mapName}_map_bkg.tga.png");
            int width, height;

            encImageBytes = TextureImportExport.Import(coverImage!, fmt, out width, out height, ref mips, afile.Metadata.TargetPlatform, []) ?? throw new Exception("Failed to encode image!");

            // Set the image data
            coverBase["image data"].AsByteArray = encImageBytes;
            coverBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
            coverBase["m_StreamData"]["offset"].AsInt = 0;
            coverBase["m_StreamData"]["size"].AsInt = 0;
            coverBase["m_StreamData"]["path"].AsString = "";

            // Save the file
            coverInfo.SetNewData(coverBase);
        }

        // Get the sprite
        AssetFileInfo coverSpriteInfo = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite).First();
        AssetTypeValueField coverSpriteBase = manager.GetBaseField(afileInst, coverSpriteInfo);

        // Set the name to {mapName}_Cover_2x
        coverSpriteBase["m_Name"].AsString = $"{mapName}_Cover_2x";

        // Save the file
        coverSpriteInfo.SetNewData(coverSpriteBase);

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Add .mod to the end of the file
        outputPackagePath = Path.Combine(outputFolder, "cache0", "Cover");
        Directory.CreateDirectory(outputPackagePath);
        uncompressedPath = Path.Combine(outputPackagePath, "temp.mod.uncompressed");
        compressedPath = Path.Combine(outputPackagePath, "temp.mod");

        using (AssetsFileWriter writer = new(uncompressedPath))
            bun.Write(writer);

        newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(File.OpenRead(uncompressedPath)));

        using (AssetsFileWriter compressedWriter = new(compressedPath))
            newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);

        newUncompressedBundle.Close();

        // Delete the uncompressed file
        File.Delete(uncompressedPath);

        // Rename the compressed file to it's md5 hash
        hash = Download.GetFileMD5(compressedPath);
        newPath = Path.Combine(outputPackagePath, $"{hash}");
        File.Move(compressedPath, newPath);

        #endregion

        // Function to get a random ID that is not used yet
        long GetRandomId()
        {
            long id = rand.NextInt64(long.MinValue, long.MaxValue);

            while (afile.Metadata.GetAssetInfo(id) is not null)
                id = rand.NextInt64(long.MinValue, long.MaxValue);

            return id;
        }

        uint[] GUID()
        {
            byte[] guidBytes = Guid.NewGuid().ToByteArray();
            uint[] uintArray = new uint[4];

            for (int j = 0; j < guidBytes.Length; j++)
                guidBytes[j] = (byte)((guidBytes[j] & 0xF0) >> 4 | (guidBytes[j] & 0x0F) << 4);

            for (int j = 0; j < 4; j++)
                uintArray[j] = BitConverter.ToUInt32(guidBytes, j * 4);

            return uintArray;
        }
    }
}
