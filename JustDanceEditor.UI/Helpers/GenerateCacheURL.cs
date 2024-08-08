using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.UbiArt;
using JustDanceEditor.Converter.Unity;

using System.Text.Encodings.Web;
using System.Text.Json;

namespace JustDanceEditor.UI.Helpers;

public static class GenerateCacheURL
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Obsolete]
    public static void ConvertToProperCache()
    {
        // Ask for the folder containing a cache_x and cache_0 folder
        string path = Question.AskFolder("Enter the path to the folder containing the cache_x and cache_0 folders: ", true);

        // Make sure this folder contains the cache_x and cache_0 folders
        if (!Directory.Exists(Path.Combine(path, "cache_x")) || !Directory.Exists(Path.Combine(path, "cache_0")))
        {
            Console.WriteLine("The folder doesn't contain the cache_x and cache_0 folders.");
            return;
        }

        // Make sure that it contains a CachingStatus.json file
        if (!File.Exists(Path.Combine(path, "CachingStatus.json")))
        {
            Console.WriteLine("The folder doesn't contain a CachingStatus.json file.");
            return;
        }

        // In the same folder, create a new folder called "output"
        string outputPath = Path.Combine(path, "output");
        Directory.CreateDirectory(outputPath);

        // In there create a folder called "SD_Cache.0000"
        string cache0Path = Path.Combine(outputPath, "SD_Cache.0000", "MapBaseCache");
        Directory.CreateDirectory(cache0Path);

        // Create a cache.json file
        string cache0Json = JDSongFactory.MapBaseJson();
        File.WriteAllText(Path.Combine(cache0Path, "json.cache"), cache0Json);

        // Inside the cache0 folder, create a folder called "Addressables"
        string addressablesPath = Path.Combine(cache0Path, "..", "Addressables");
        Directory.CreateDirectory(addressablesPath);
        string addressablesJsonCache = JDSongFactory.AddressablesJson();
        File.WriteAllText(Path.Combine(addressablesPath, "json.cache"), addressablesJsonCache);

        // Parse the CachingStatus.json file
        JDCacheJSON cachingStatus = JsonSerializer.Deserialize<JDCacheJSON>(File.ReadAllText(Path.Combine(path, "CachingStatus.json")))!;

        // Convert to a list of key value pairs
        List<KeyValuePair<string, JDSong>> cachingStatusList = [.. cachingStatus.MapsDict];

        // Sort by original just dance version
        cachingStatusList.Sort((x, y) => x.Value.SongDatabaseEntry.OriginalJDVersion.CompareTo(y.Value.SongDatabaseEntry.OriginalJDVersion));

        // Cache counter, starting at 1
        uint cacheNumber = 0;
        uint cacheSize = 0;
        uint JDVersion = 0;
        string cacheXPath = "";

        // cacheNumber -> JDVersion map
        Dictionary<uint, uint> cacheNumberJDVersion = [];

        foreach (KeyValuePair<string, JDSong> song in cachingStatusList)
        {
            // If the original just dance version is different, increase the cache number and reset the JDVersion
            if (song.Value.SongDatabaseEntry.OriginalJDVersion != JDVersion)
            {
                cacheNumber++;
                cacheSize = 0;
                JDVersion = song.Value.SongDatabaseEntry.OriginalJDVersion;

                cacheXPath = Path.Combine(outputPath, $"SD_Cache.{cacheNumber:X4}");
                Directory.CreateDirectory(cacheXPath);

                cacheNumberJDVersion[cacheNumber] = JDVersion;

                // Print the mapping
                Console.WriteLine($"  JD {JDVersion:D4} -> SD_Cache.{cacheNumber:X4}");
            }

            // If the cache size is bigger than 3GB, increase the cache number and reset the cache size
            // This could be 4GB, but might as well be safe and use 3.5GB instead, just in case
            if (cacheSize > 3_500_000_000)
            {
                cacheNumber++;
                cacheSize = 0;

                cacheXPath = Path.Combine(outputPath, $"SD_Cache.{cacheNumber:X4}");
                Directory.CreateDirectory(cacheXPath);
            }

            // Copy the files from the original cache0 folder to the new cache0 folder
            string originalCache0Path = Path.Combine(path, "cache_0", song.Key);
            string newCache0Path = Path.Combine(cache0Path, song.Key);

            cacheSize += Copy.CopyFolder(originalCache0Path, newCache0Path);

            // Copy the files from the original cache{x} folder to the new cache{x} folder
            string originalCachexPath = Path.Combine(path, "cache_x", song.Key);
            string newCachexPath = Path.Combine(cacheXPath, song.Key);

            cacheSize += Copy.CopyFolder(originalCachexPath, newCachexPath);

            // Change the path in the json file
            song.Value.AssetFilesDict.CoachesSmall.FilePath = $"/CacheStorage_{cacheNumber}/{song.Key}/CoachesSmall/{song.Value.AssetFilesDict.CoachesSmall.Hash}";
            song.Value.AssetFilesDict.CoachesLarge.FilePath = $"/CacheStorage_{cacheNumber}/{song.Key}/CoachesLarge/{song.Value.AssetFilesDict.CoachesLarge.Hash}";
            song.Value.AssetFilesDict.Audio_opus.FilePath = $"/CacheStorage_{cacheNumber}/{song.Key}/Audio_opus/{song.Value.AssetFilesDict.Audio_opus.Hash}";
            song.Value.AssetFilesDict.Video_HIGH_vp9_webm.FilePath = $"/CacheStorage_{cacheNumber}/{song.Key}/Video_HIGH_vp9_webm/{song.Value.AssetFilesDict.Video_HIGH_vp9_webm.Hash}";
            song.Value.AssetFilesDict.MapPackage.FilePath = $"/CacheStorage_{cacheNumber}/{song.Key}/MapPackage/{song.Value.AssetFilesDict.MapPackage.Hash}";

            if (song.Value.HasSongTitleInCover == true)
                song.Value.AssetFilesDict.SongTitleLogo!.FilePath = $"/CacheStorage_{cacheNumber}/{song.Key}/Cover/{song.Value.AssetFilesDict.SongTitleLogo.Hash}";

            // Create the json.cache file
            string jsonCachePath = Path.Combine(newCachexPath, "json.cache");
            string json = JDSongFactory.CacheJson(cacheNumber, song.Key);

            File.WriteAllText(jsonCachePath, json);
        }

        // Now reverse the list as we want the newest songs to be at the top in the CachingStatus.json file
        cachingStatusList.Reverse();

        // Convert the list back to a dict while keeping the order
        cachingStatus.MapsDict = cachingStatusList.ToDictionary(x => x.Key, x => x.Value);

        // Convert the dict to json and save it in the cache0 folder
        string jsonCachingStatus = JsonSerializer.Serialize(cachingStatus, JsonSerializerOptions);
        File.WriteAllText(Path.Combine(cache0Path, "CachingStatus.json"), jsonCachingStatus);

        // In the input folder, create a mapping.txt file
        string mappingPath = Path.Combine(path, "mapping.txt");
        string mapping = "";

        foreach (KeyValuePair<uint, uint> pair in cacheNumberJDVersion)
            mapping += $"JD {pair.Value:D4} -> SD_Cache.{pair.Key:X4}\n";

        File.WriteAllText(mappingPath, mapping);

        Console.WriteLine($"Finished converting {cachingStatusList.Count} songs to {cacheNumber} caches.");
    }

    [Obsolete]
    public static void GenerateCacheWithExistingData()
    {
        // First off, where is the input data?
        string? path = Question.AskFolder("Enter the path for the input data: ", true);

        // This must contain the folder "jd-s3.cdn.ubi.com" and one json file
        if (!Directory.Exists(Path.Combine(path, "jd-s3.cdn.ubi.com")))
        {
            Console.WriteLine("The path doesn't contain the folder \"jd-s3.cdn.ubi.com\".");
            return;
        }

        // Get the json file
        string[] jsonFiles = Directory.GetFiles(path, "*.json");
        if (jsonFiles.Length == 0)
        {
            Console.WriteLine("The path doesn't contain a json file.");
            return;
        }

        // If there are multiple json files, give an error
        if (jsonFiles.Length > 1)
        {
            Console.WriteLine("The path contains multiple json files.");
            return;
        }

        // Where do we want to save the cache?
        string? savePath = Question.AskFolder("Enter the path for where to save the cache: ", false);
        Directory.CreateDirectory(savePath);

        // Ask for the cache number
        uint cacheNumber = 1;

        // Create the cache_x and cache_0 folders
        string cacheXPath = Path.Combine(savePath, "cache_x");
        string cache0Path = Path.Combine(savePath, "cache_0");
        Directory.CreateDirectory(cacheXPath);
        Directory.CreateDirectory(cache0Path);

        // Private and public path
        string privatePath = Path.Combine(path, "jd-s3.cdn.ubi.com", "private", "jdnext", "maps");
        string publicPath = Path.Combine(path, "jd-s3.cdn.ubi.com", "public", "jdnext", "maps");

        // Get all folder names in the private path
        string[] mapIDs = Directory.GetDirectories(privatePath).Select(Path.GetFileName).ToArray()!;

        Console.WriteLine($"Found {mapIDs.Length} maps.");

        // Parse the json file into a JDNextUbiData
        Dictionary<string, JDNextUbiMapData> json = JsonSerializer.Deserialize<Dictionary<string, JDNextUbiMapData>>(File.ReadAllText(jsonFiles[0]))!;
        Dictionary<string, JDSong> MapsDict = [];

        // Print each map
        foreach (string map in mapIDs)
            Console.WriteLine($"\t- {json[map].title}");

        Console.WriteLine();
        Console.WriteLine("Processing maps...");

        foreach (string map in mapIDs)
        {
            // Create the map folder
            string map0Path = Path.Combine(cache0Path, map);
            string mapXPath = Path.Combine(cacheXPath, map);
            Directory.CreateDirectory(map0Path);
            Directory.CreateDirectory(mapXPath);

            string puMapPath = Path.Combine(publicPath, map);
            string prMapPath = Path.Combine(privatePath, map);

            /// Cache 0
            // Create the videoPreview_MID_vp9_webm folder
            string videoPreviewMidVp9WebmPath = Path.Combine(map0Path, "VideoPreview_MID_vp9_webm");
            Directory.CreateDirectory(videoPreviewMidVp9WebmPath);
            string videoPreviewMidVp9WebmFile = Directory.GetFiles(Path.Combine(puMapPath, "videoPreview_MID.vp9.webm"))[0];
            string videoPreviewMidVp9WebmHash = Download.GetFileMD5(videoPreviewMidVp9WebmFile);
            File.Copy(videoPreviewMidVp9WebmFile, Path.Combine(videoPreviewMidVp9WebmPath, videoPreviewMidVp9WebmHash));

            // Create the audioPreview_opus folder
            string audioPreviewOpusPath = Path.Combine(map0Path, "AudioPreview_opus");
            Directory.CreateDirectory(audioPreviewOpusPath);
            string audioPreviewOpusFile = Directory.GetFiles(Path.Combine(puMapPath, "audioPreview.opus"))[0];
            string audioPreviewOpusHash = Download.GetFileMD5(audioPreviewOpusFile);
            File.Copy(audioPreviewOpusFile, Path.Combine(audioPreviewOpusPath, audioPreviewOpusHash));

            // Create the cover folder
            string coverPath = Path.Combine(map0Path, "Cover");
            Directory.CreateDirectory(coverPath);
            string coverFile = Directory.GetFiles(Path.Combine(puMapPath, "nx", "cover"))[0];
            string coverHash = Download.GetFileMD5(coverFile);
            File.Copy(coverFile, Path.Combine(coverPath, coverHash));

            // Check if the map has a songTitleLogo
            bool hasSongTitleLogo = Directory.Exists(Path.Combine(puMapPath, "nx", "songTitleLogo"));
            string? songTitleLogoPath = null;
            string? songTitleLogoHash = null;
            if (hasSongTitleLogo)
            {
                // Create the songTitleLogo folder
                songTitleLogoPath = Path.Combine(map0Path, "songTitleLogo");
                Directory.CreateDirectory(songTitleLogoPath);
                string songTitleLogoFile = Directory.GetFiles(Path.Combine(puMapPath, "nx", "songTitleLogo"))[0];
                songTitleLogoHash = Download.GetFileMD5(songTitleLogoFile);
                File.Copy(songTitleLogoFile, Path.Combine(songTitleLogoPath, songTitleLogoHash));
            }

            /// Cache x
            // Create the audio_opus folder
            string audioOpusPath = Path.Combine(mapXPath, "Audio_opus");
            Directory.CreateDirectory(audioOpusPath);
            string audioOpusFile = Directory.GetFiles(Path.Combine(prMapPath, "audio.opus"))[0];
            string audioOpusHash = Download.GetFileMD5(audioOpusFile);
            File.Copy(audioOpusFile, Path.Combine(audioOpusPath, audioOpusHash));

            // Create the coachesLarge folder
            string coachesLargePath = Path.Combine(mapXPath, "CoachesLarge");
            Directory.CreateDirectory(coachesLargePath);
            string coachesLargeFile = Directory.GetFiles(Path.Combine(puMapPath, "nx", "coachesLarge"))[0];
            string coachesLargeHash = Download.GetFileMD5(coachesLargeFile);
            File.Copy(coachesLargeFile, Path.Combine(coachesLargePath, coachesLargeHash));

            // Create the coachesSmall folder
            string coachesSmallPath = Path.Combine(mapXPath, "CoachesSmall");
            Directory.CreateDirectory(coachesSmallPath);
            string coachesSmallFile = Directory.GetFiles(Path.Combine(puMapPath, "nx", "coachesSmall"))[0];
            string coachesSmallHash = Download.GetFileMD5(coachesSmallFile);
            File.Copy(coachesSmallFile, Path.Combine(coachesSmallPath, coachesSmallHash));

            // Create the mapPackage folder
            string mapPackagePath = Path.Combine(mapXPath, "MapPackage");
            Directory.CreateDirectory(mapPackagePath);
            string mapPackageFile = Directory.GetFiles(Path.Combine(prMapPath, "nx", "mapPackage"))[0];
            string mapPackageHash = Download.GetFileMD5(mapPackageFile);
            File.Copy(mapPackageFile, Path.Combine(mapPackagePath, mapPackageHash));

            // Create the video_HIGH_vp9_webm folder
            string videoHighVp9WebmPath = Path.Combine(mapXPath, "Video_HIGH_vp9_webm");
            Directory.CreateDirectory(videoHighVp9WebmPath);
            string videoHighVp9WebmFile = Directory.GetFiles(Path.Combine(prMapPath, "video_HIGH.vp9.webm"))[0];
            string videoHighVp9WebmHash = Download.GetFileMD5(videoHighVp9WebmFile);
            File.Copy(videoHighVp9WebmFile, Path.Combine(videoHighVp9WebmPath, videoHighVp9WebmHash));

            // Create the json.cache file
            string jsonCachePath = Path.Combine(mapXPath, "json.cache");
            string jsonCache = JDSongFactory.CacheJson(cacheNumber, map);
            File.WriteAllText(jsonCachePath, jsonCache);

            JDNextUbiMapData songData = json[map];

            // Generate the JDSong
            JDSong song = JDSongFactory.CreateSong((SongDatabaseEntry)songData, cacheNumber, coverHash, coachesSmallHash, coachesLargeHash, audioPreviewOpusHash, videoPreviewMidVp9WebmHash, audioOpusHash, videoHighVp9WebmHash, mapPackageHash, songTitleLogoHash, map);

            // Add the song to the dict
            MapsDict.Add(map, song);
        }

        // Convert the dict to json and save it
        string cachingStatus = JsonSerializer.Serialize(new JDCacheJSON
        {
            MapsDict = MapsDict
        }, JsonSerializerOptions);

        File.WriteAllText(Path.Combine(savePath, "CachingStatus.json"), cachingStatus);

        Console.WriteLine();
        Console.WriteLine("Done.");
    }

    [Obsolete]
    public static void GenerateCacheWithUrls()
    {
        // First get all relevant data
        Console.Write("Enter the song title: ");
        string songTitle = Console.ReadLine()!;
        Console.Write("Enter the artist name: ");
        string artistName = Console.ReadLine()!;
        Console.Write("Enter the original just dance version: ");
        uint originalJustDanceVersion = uint.Parse(Console.ReadLine()!);
        Console.Write("Enter the coach count: ");
        uint coachCount = uint.Parse(Console.ReadLine()!);
        Console.Write("Enter the difficulty: ");
        uint difficulty = uint.Parse(Console.ReadLine()!);
        Console.Write("Enter the sweat difficulty: ");
        uint sweatDifficulty = uint.Parse(Console.ReadLine()!);

        uint cacheNumber = (uint)Question.AskNumber("Enter the cache number (can be empty): ", 1);

        // Enter
        Console.WriteLine();

        // Ask for a GUID type 4, or whether to generate one
        Console.Write("Enter the GUID, or leave empty to generate one: ");
        string? guid = Console.ReadLine();
        if (string.IsNullOrEmpty(guid))
            guid = Guid.NewGuid().ToString();

        // Ask for the urls
        Console.WriteLine("Enter the urls for the following assets:");
        string coachesSmallUrl = Question.AskForUrl("coachesSmall");
        string coachesLargeUrl = Question.AskForUrl("coachesLarge");
        string coverUrl = Question.AskForUrl("cover");
        string songTitleLogoUrl = Question.AskForUrl("songTitleLogo", true);
        bool hasSongTitleInCover = string.IsNullOrEmpty(songTitleLogoUrl);
        string audioPreviewOpusUrl = Question.AskForUrl("audioPreview.opus");
        string videoPreviewMidVp9WebmUrl = Question.AskForUrl("videoPreview_MID.vp9.webm");
        string videoHighVp9WebmUrl = Question.AskForUrl("video_HIGH.vp9.webm");
        string audioOpusUrl = Question.AskForUrl("audio.opus");
        string mapPackageUrl = Question.AskForUrl("mapPackage");

        // Create the cache
        // Ask for a cache location
        string path = Question.AskFolder("Enter the path for where to save the cache: ");

        // The place to put everything from cache0
        string cache0Path = Path.Combine(path, "cache0", guid);
        {
            Directory.CreateDirectory(cache0Path);

            // Download the assets and place them in the correct folder,
            string tempDirPath = Path.Combine(cache0Path, "AudioPreview_opus");
            audioPreviewOpusUrl = Download.DownloadFileMD5(audioPreviewOpusUrl, tempDirPath, false);
            tempDirPath = Path.Combine(cache0Path, "Cover");
            coverUrl = Download.DownloadFileMD5(coverUrl, tempDirPath, false);
            if (hasSongTitleInCover)
            {
                tempDirPath = Path.Combine(cache0Path, "songTitleLogo");
                songTitleLogoUrl = Download.DownloadFileMD5(songTitleLogoUrl, tempDirPath, false);
            }

            tempDirPath = Path.Combine(cache0Path, "VideoPreview_MID_vp9_webm");
            videoPreviewMidVp9WebmUrl = Download.DownloadFileMD5(videoPreviewMidVp9WebmUrl, tempDirPath, false);
        }

        // The place to put everything from cache{x}
        // Cachename is
        string cachexPath = Path.Combine(path, $"cache{cacheNumber:X}", guid);
        {
            Directory.CreateDirectory(Path.Combine(cachexPath));

            // Download the assets and place them in the correct folder,
            string tempDirPath = Path.Combine(cachexPath, "Audio_opus");
            audioOpusUrl = Download.DownloadFileMD5(audioOpusUrl, tempDirPath, false);
            tempDirPath = Path.Combine(cachexPath, "CoachesLarge");
            coachesLargeUrl = Download.DownloadFileMD5(coachesLargeUrl, tempDirPath, false);
            tempDirPath = Path.Combine(cachexPath, "CoachesSmall");
            coachesSmallUrl = Download.DownloadFileMD5(coachesSmallUrl, tempDirPath, false);
            tempDirPath = Path.Combine(cachexPath, "MapPackage");
            mapPackageUrl = Download.DownloadFileMD5(mapPackageUrl, tempDirPath, false);
            tempDirPath = Path.Combine(cachexPath, "Video_HIGH_vp9_webm");
            videoHighVp9WebmUrl = Download.DownloadFileMD5(videoHighVp9WebmUrl, tempDirPath, false);
        }

        // Create the json.cache file
        string jsonCachePath = Path.Combine(cachexPath, "json.cache");
        string json = JDSongFactory.CacheJson(cacheNumber, guid);
        File.WriteAllText(jsonCachePath, json);

        // Ask for the parentMapId
        Console.WriteLine("You are required to look for the parentMapId. You can find this on the wiki.");
        Console.Write("Enter the ParentMapId: ");
        string parentMapId = Console.ReadLine()!;

        //// Open the webm file and get the duration
        //string webmPath = Path.Combine(cachexPath, "Video_HIGH_vp9_webm", videoHighVp9WebmUrl);
        //using MediaFoundationReader reader = new(webmPath);
        //double songDuration = reader.TotalTime.TotalSeconds;

        // Ask for the song duration
        Console.Write("Enter the song duration in seconds: ");
        double songDuration = double.Parse(Console.ReadLine()!);

        // Create a string JDSong dict
        Dictionary<string, JDSong> MapsDict;

        // First create the SongDatabaseEntry
        SongDatabaseEntry songDatabaseEntry = new()
        {
            MapId = guid,
            ParentMapId = parentMapId,
            Title = songTitle,
            Artist = artistName,
            Credits = "",
            LyricsColor = "",
            MapLength = songDuration,
            OriginalJDVersion = originalJustDanceVersion,
            CoachCount = coachCount,
            Difficulty = difficulty,
            SweatDifficulty = sweatDifficulty,
            Tags = [],
            TagIds = [],
            SearchTagsLocIds = [],
            CoachNamesLocIds = [],
            HasSongTitleInCover = hasSongTitleInCover
        };

        // Create the JDSong
        JDSong song = JDSongFactory.CreateSong(songDatabaseEntry, cacheNumber, coverUrl, coachesSmallUrl, coachesLargeUrl, audioPreviewOpusUrl, videoPreviewMidVp9WebmUrl, audioOpusUrl, videoHighVp9WebmUrl, mapPackageUrl, songTitleLogoUrl, guid);

        // Add the song to the dict
        MapsDict = new()
        {
            [guid] = song
        };

        // Convert the dict to json and print it to the console
        json = JsonSerializer.Serialize(new JDCacheJSON
        {
            MapsDict = MapsDict
        }, JsonSerializerOptions);

        Console.WriteLine();
        Console.WriteLine(json);
        Console.WriteLine();
        Console.WriteLine("Add the json to the cache file and save it. Make sure to add in the correct place.");
    }

    [Obsolete]
    public static void GenerateCacheWithSongDBAndUrls()
    {
        // First off, where do we want to save the cache?
        string? path = Question.AskFolder("Enter the path for where to save the cache: ", false);
        Directory.CreateDirectory(path);

        // Ask for the cache number
        Console.Write("Enter the cache number: ");
        uint cacheNumber = (uint)Question.AskNumber("Enter the cache number: ", 1);

        // First get the songDB
        string songDBPath = Question.AskFile("Enter the songDB path: ", true);

        // Ask for a secondary songDB, can be empty
        string? songDBSecondaryPath = Question.AskFile("Enter the secondary songDB path, or leave empty: ", false);

        // Read the songDB
        string songDBJson = File.ReadAllText(songDBPath);

        // Parse the songDB
        Dictionary<string, SongDBSongData> songDB = JsonSerializer.Deserialize<Dictionary<string, SongDBSongData>>(songDBJson)!;

        // If there's a secondary songDB, read it and parse it, merge it with the songDB but overwrite existing entries
        if (!string.IsNullOrEmpty(songDBSecondaryPath))
        {
            string songDBSecondaryJson = File.ReadAllText(songDBSecondaryPath);
            Dictionary<string, SongDBSongData> songDBSecondary = JsonSerializer.Deserialize<Dictionary<string, SongDBSongData>>(songDBSecondaryJson)!;

            foreach (KeyValuePair<string, SongDBSongData> songData in songDBSecondary)
                // If the songDB doesn't contain the song, add it
                if (!songDB.ContainsKey(songData.Key))
                    songDB.Add(songData.Key, songData.Value);
        }

        Dictionary<string, JDSong> MapsDict = [];

        while (true)
        {
            SongDBSongData? songData = null;

            while (songData == null)
            {
                // Ask for the song title
                Console.Write("Enter the song title (or exit): ");
                string songTitle = Console.ReadLine()!;

                if (songTitle is "exit" or "")
                    break;

                // Try to find all songs with the song title, either containing or equal to
                List<SongDBSongData> songs = songDB.Values.Where(x => x.title.Contains(songTitle, StringComparison.OrdinalIgnoreCase) || x.title.Equals(songTitle, StringComparison.OrdinalIgnoreCase)).ToList();

                // If there are no songs, print an error and continue
                if (songs.Count == 0)
                {
                    Console.WriteLine("There are no songs with that title.");
                    continue;
                }

                // If there's only one song, ask if it's the correct one
                if (songs.Count == 1)
                {
                    Console.WriteLine($"Is this the correct song? (y/n)");
                    Console.WriteLine($"Parent Map Name: {songs[0].parentMapName}");
                    Console.WriteLine($"Title: {songs[0].title}");
                    Console.WriteLine($"Artist: {songs[0].artist}");
                    Console.WriteLine($"Original JD Version: {songs[0].originalJDVersion}");
                    Console.WriteLine($"Coach Count: {songs[0].coachCount}");
                    Console.WriteLine($"Map Length: {songs[0].mapLength}");
                    Console.WriteLine($"Map Name: {songs[0].mapName}");
                    Console.WriteLine();

                    // Ask if it's the correct song
                    string? answer = Console.ReadLine();

                    // If the answer is yes, set the songData to the song
                    if (answer == "y")
                        songData = songs[0];
                    else
                        continue;
                }
                else
                {
                    // If there are multiple songs, ask for the song number
                    Console.WriteLine("There are multiple songs with that title. Enter the number of the song you want to use.");
                    Console.WriteLine("0) Cancel");
                    for (int i = 0; i < songs.Count; i++)
                    {
                        Console.WriteLine($"{i + 1}) {songs[i].title} - {songs[i].mapName} - {songs[i].originalJDVersion} - Coach Count: {songs[i].coachCount}");
                    }

                    uint choice = (uint)Question.AskNumber("Enter the number of the song you want to use: ", 0, songs.Count);

                    // If the choice is 0, exit
                    if (choice == 0)
                        continue;

                    // Set the songData to the song
                    songData = songs[(int)choice - 1];
                }
            }

            // If the songData is null, exit
            if (songData == null)
                break;

            // If we are here, we have a song
            // Print the song
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Map Name: {songData.mapName}");
            Console.ForegroundColor = ConsoleColor.Gray;

            // Ask for the urls
            Console.WriteLine("Enter the urls for the following assets:");
            string coachesSmallUrl = Question.AskForUrl("coachesSmall");
            string coachesLargeUrl = Question.AskForUrl("coachesLarge");
            string coverUrl = Question.AskForUrl("cover");
            string songTitleLogoUrl = Question.AskForUrl("songTitleLogo", true);
            bool hasSongTitleInCover = !string.IsNullOrEmpty(songTitleLogoUrl);
            string audioPreviewOpusUrl = Question.AskForUrl("audioPreview.opus");
            string videoPreviewMidVp9WebmUrl = Question.AskForUrl("videoPreview_MID.vp9.webm");
            string videoHighVp9WebmUrl = Question.AskForUrl("video_HIGH.vp9.webm");
            string audioOpusUrl = Question.AskForUrl("audio.opus");
            string mapPackageUrl = Question.AskForUrl("mapPackage");

            // Generate a GUID
            string guid = Guid.NewGuid().ToString();

            // This didn't work so we'll use a hardcoded value, really don't know how this format works
            //string audioTrackData = AudioTrackConverter.ConvertAudioTrack(songData.audioPreviewData);

            // Create the cache0 folder
            // Cachename is SD_Cache.XXXX, where XXXX is the cache number in hex without the 0x prefix and padded to 4 characters
            string cacheName = $"SD_Cache.0000";
            string cache0Path = Path.Combine(path, cacheName, guid);
            {
                Directory.CreateDirectory(cache0Path);

                // Download the assets and place them in the correct folder,
                string tempDirPath = Path.Combine(cache0Path, "AudioPreview_opus");
                audioPreviewOpusUrl = Download.DownloadFileMD5(audioPreviewOpusUrl, tempDirPath, false);
                tempDirPath = Path.Combine(cache0Path, "Cover");
                coverUrl = Download.DownloadFileMD5(coverUrl, tempDirPath, false);
                if (hasSongTitleInCover)
                {
                    tempDirPath = Path.Combine(cache0Path, "songTitleLogo");
                    songTitleLogoUrl = Download.DownloadFileMD5(songTitleLogoUrl, tempDirPath, false);
                }

                tempDirPath = Path.Combine(cache0Path, "VideoPreview_MID_vp9_webm");
                videoPreviewMidVp9WebmUrl = Download.DownloadFileMD5(videoPreviewMidVp9WebmUrl, tempDirPath, false);
            }

            // Create the cache{x} folder
            string cachexPath = Path.Combine(path, $"SD_Cache.{cacheNumber:X4}", guid);
            {
                Directory.CreateDirectory(Path.Combine(cachexPath));

                // Download the assets and place them in the correct folder,
                string tempDirPath = Path.Combine(cachexPath, "Audio_opus");
                audioOpusUrl = Download.DownloadFileMD5(audioOpusUrl, tempDirPath, false);
                tempDirPath = Path.Combine(cachexPath, "CoachesLarge");
                coachesLargeUrl = Download.DownloadFileMD5(coachesLargeUrl, tempDirPath, false);
                tempDirPath = Path.Combine(cachexPath, "CoachesSmall");
                coachesSmallUrl = Download.DownloadFileMD5(coachesSmallUrl, tempDirPath, false);
                tempDirPath = Path.Combine(cachexPath, "MapPackage");
                mapPackageUrl = Download.DownloadFileMD5(mapPackageUrl, tempDirPath, false);
                tempDirPath = Path.Combine(cachexPath, "Video_HIGH_vp9_webm");
                videoHighVp9WebmUrl = Download.DownloadFileMD5(videoHighVp9WebmUrl, tempDirPath, false);
            }

            // Create the json.cache file
            string jsonCachePath = Path.Combine(cachexPath, "json.cache");
            string json = JDSongFactory.CacheJson(cacheNumber, guid);

            // Write the json to the file
            File.WriteAllText(jsonCachePath, json);

            // First create the SongDatabaseEntry
            SongDatabaseEntry songDatabaseEntry = new()
            {
                MapId = guid,
                ParentMapId = songData.parentMapName,
                Title = songData.title,
                Artist = songData.artist,
                Credits = songData.credits,
                LyricsColor = songData.lyricsColor,
                MapLength = songData.mapLength,
                OriginalJDVersion = songData.originalJDVersion,
                CoachCount = songData.coachCount,
                Difficulty = songData.difficulty,
                SweatDifficulty = songData.sweatDifficulty,
                Tags = ["Main"],
                TagIds = [],
                SearchTagsLocIds = [],
                CoachNamesLocIds = [],
                HasSongTitleInCover = hasSongTitleInCover
            };

            // Generate the JDSong
            JDSong song = JDSongFactory.CreateSong(songDatabaseEntry, cacheNumber, coverUrl, coachesSmallUrl, coachesLargeUrl, audioPreviewOpusUrl, videoPreviewMidVp9WebmUrl, audioOpusUrl, videoHighVp9WebmUrl, mapPackageUrl, songTitleLogoUrl, guid);

            // Add the song to the dict
            MapsDict.Add(guid, song);

            // Print that we're done
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Finished setting up the song {songData.title} - {songData.mapName} - {songData.originalJDVersion}");
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        // Convert the dict to json and print it to the console as well as save it to a file
        string jsonText = JsonSerializer.Serialize(MapsDict, JsonSerializerOptions);

        // If the jsonText is empty, exit
        if (string.IsNullOrEmpty(jsonText))
        {
            Console.WriteLine("Exiting.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine(jsonText);
        Console.WriteLine();

        // Write the json to a file
        string jsonPath = Path.Combine(path, "SongCache.json");
        File.WriteAllText(jsonPath, jsonText);

        // Print to a file the following format: "GUID - Song Title - Map Name - Original JD Version"
        string text = string.Join(Environment.NewLine, MapsDict.Select(x => $"\"{x.Key}\" - \"{x.Value.SongDatabaseEntry.Title}\" - \"{x.Value.SongDatabaseEntry.MapId}\" - \"{x.Value.SongDatabaseEntry.OriginalJDVersion}\""));
        string textPath = Path.Combine(path, "SongCache.txt");
        File.WriteAllText(textPath, text);

        Console.WriteLine("Done!");
    }
}
