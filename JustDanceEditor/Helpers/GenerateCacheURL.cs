using System.Text.Encodings.Web;
using System.Text.Json;

namespace JustDanceEditor.Helpers;
internal class GenerateCacheURL
{
    // This is the hardcoded value, taken from Encanto/We don't talk about Bruno
    static readonly string audioTrackData = "{\"Markers\":[0,16552,33103,49655,66207,82759,99310,115862,132414,148966,165517,182069,198621,215172,231724,248276,264828,281379,297931,314483,331034,347586,364138,380690,397241,413793,430345,446897,463448,480000,496552,513103,529655,546207,562759,579310,595862,612414,628966,645517,662069,678621,695172,711724,728276,744828,761379,777931,794483,811034,827586,844138,860690,877241,893793,910345,926897,943448,960000,976552,993103,1009655,1026207,1042759,1059310,1075862,1092414,1108966,1125517,1142069,1158621,1175172,1191724,1208276,1224828,1241379,1257931,1274483,1291034,1307586,1324138,1340690,1357241,1373793,1390345,1406897,1423448,1440000,1456552,1473103,1489655,1506207,1522759,1539310,1555862,1572414,1588966,1605517,1622069,1638621,1655172,1671724,1688276,1704828,1721379,1737931,1754483,1771034,1787586,1804138,1820690,1837241,1853793,1870345,1886897,1903448,1920000,1936552,1953103,1969655,1986207,2002759,2019310,2035862,2052414,2068966,2085517,2102069,2118621,2135172,2151724,2168276,2184828,2201379,2217931,2234483,2251034,2267586,2284138,2300690,2317241,2333793,2350345,2366897,2383448,2400000,2416552,2433103,2449655,2466207,2482759,2499310,2515862,2532414,2548966,2565517,2582069,2598621,2615172,2631724,2648276,2664828,2681379,2697931,2714483,2731034,2747586,2764138,2780690,2797241,2813793,2830345,2846897,2863448,2880000,2896552,2913103,2929655,2946207,2962759,2979310,2995862,3012414,3028966,3045517,3062069,3078621,3095172,3111724,3128276,3144828,3161379,3177931,3194483,3211034,3227586,3244138,3260690,3277241,3293793,3310345,3326897,3343448,3360000,3376552,3393103,3409655,3426207,3442759,3459310,3475862,3492414,3508966,3525517,3542069,3558621,3575172,3591724,3608276,3624828,3641379,3657931,3674483,3691034,3707586,3724138,3740690,3757241,3773793,3790345,3806897,3823448,3840000,3856552,3873103,3889655,3906207,3922759,3939310,3955862,3972414,3988966,4005517,4022069,4038621,4055172,4071724,4088276,4104828,4121379,4137931,4154483,4171034,4187586,4204138,4220690,4237241,4253793,4270345,4286897,4303448,4320000,4336552,4353103,4369655,4386207,4402759,4419310,4435862,4452414,4468966,4485517,4502069,4518621,4535172,4551724,4568276,4584828,4601379,4617931,4634483,4651034,4667586,4684138,4700690,4717241,4733793,4750345,4766897,4783448,4800000,4816552,4833103,4849655,4866207,4882759,4899310,4915862,4932414,4948966,4965517,4982069,4998621,5015172,5031724,5048276,5064828,5081379,5097931,5114483,5131034,5147586,5164138,5180690,5197241,5213793,5230345,5246897,5263448,5280000,5296552,5313103,5329655,5346207,5362759,5379310,5395862,5412414,5428966,5445517,5462069,5478621,5495172,5511724,5528276,5544828,5561379,5577931,5594483,5611034,5627586,5644138,5660690,5677241,5693793,5710345,5726897,5743448,5760000,5776552,5793103,5809655,5826207,5842759,5859310,5875862,5892414,5908966,5925517,5942069,5958621,5975172,5991724,6008276,6024828,6041379,6057931,6074483,6091034,6107586,6124138,6140690,6157241,6173793,6190345,6206897,6223448,6240000,6256552,6273103,6289655,6306207,6322759,6339310,6355862,6372414,6388966,6405517,6422069,6438621,6455172,6471724,6488276,6504828,6521379,6537931,6554483,6571034,6587586,6604138,6620690,6637241,6653793,6670345,6686897,6703448,6720000,6736552,6753103,6769655,6786207,6802759,6819310,6835862,6852414,6868966,6885517,6902069,6918621,6935172,6951724,6968276,6984828,7001379,7017931,7034483,7051034,7067586,7084138,7100690,7117241,7133793,7150345,7166897,7183448,7200000,7216552,7233103,7249655,7266207,7282759,7299310,7315862,7332414,7348966,7365517,7382069,7398621,7415172,7431724,7448276,7464828,7481379,7497931,7514483,7531034,7547586,7564138,7580690,7597241,7613793,7630345,7646897,7663448,7680000,7696552,7713103,7729655,7746207,7762759,7779310,7795862,7812414,7828966,7845517,7862069,7878621,7895172,7911724,7928276,7944828,7961379,7977931,7994483,8011034,8027585],\"StartBeat\":-8.0,\"EndBeat\":481.0,\"VideoStartTime\":-14.828,\"PreviewEntry\":327.0,\"PreviewLoopStart\":329.0,\"PreviewLoopEnd\":401.0}";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void GenerateCacheWithUrls()
    {
        // First get all relevant data
        Console.Write("Enter the song title: ");
        string? songTitle = Console.ReadLine();
        Console.Write("Enter the artist name: ");
        string? artistName = Console.ReadLine();
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
        Console.Write("CoachesSmall: ");
        string? coachesSmallUrl = Console.ReadLine();
        // If the url doesn't contain "coachesSmall" ask if the user is sure, if not ask for the url again
        while (!coachesSmallUrl.Contains("coachesSmall"))
        {
            Console.WriteLine("The url doesn't contain \"coachesSmall\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("CoachesSmall: ");
            coachesSmallUrl = Console.ReadLine();
        }

        Console.Write("CoachesLarge: ");
        string? coachesLargeUrl = Console.ReadLine();
        // If the url doesn't contain "coachesLarge" ask if the user is sure, if not ask for the url again
        while (!coachesLargeUrl.Contains("coachesLarge"))
        {
            Console.WriteLine("The url doesn't contain \"coachesLarge\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("CoachesLarge: ");
            coachesLargeUrl = Console.ReadLine();
        }

        Console.Write("Cover: ");
        string? coverUrl = Console.ReadLine();
        // If the url doesn't contain "cover" ask if the user is sure, if not ask for the url again
        while (!coverUrl.Contains("cover"))
        {
            Console.WriteLine("The url doesn't contain \"cover\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("Cover: ");
            coverUrl = Console.ReadLine();
        }

        string? songTitleLogoUrl = "";
        Console.Write("songTitleLogo (can be empty): ");
        songTitleLogoUrl = Console.ReadLine();
        bool hasSongTitleInCover = !string.IsNullOrEmpty(songTitleLogoUrl);
        // If the url isn't empty and doesn't contain "songTitleLogo" ask if the user is sure, if not ask for the url again
        while (!string.IsNullOrEmpty(songTitleLogoUrl) && !songTitleLogoUrl.Contains("songTitleLogo"))
        {
            Console.WriteLine("The url doesn't contain \"songTitleLogo\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("songTitleLogo (can be empty): ");
            songTitleLogoUrl = Console.ReadLine();
        }

        Console.Write("AudioPreview_opus: ");
        string? audioPreviewOpusUrl = Console.ReadLine();
        // If the url doesn't contain "audioPreview.opus" ask if the user is sure, if not ask for the url again
        while (!audioPreviewOpusUrl.Contains("audioPreview.opus"))
        {
            Console.WriteLine("The url doesn't contain \"audioPreview.opus\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("AudioPreview_opus: ");
            audioPreviewOpusUrl = Console.ReadLine();
        }

        Console.Write("VideoPreview_MID_vp9_webm: ");
        string? videoPreviewMidVp9WebmUrl = Console.ReadLine();
        // If the url doesn't contain "videoPreview_MID.vp9.webm" ask if the user is sure, if not ask for the url again
        while (!videoPreviewMidVp9WebmUrl.Contains("videoPreview_MID.vp9.webm"))
        {
            Console.WriteLine("The url doesn't contain \"videoPreview_MID.vp9.webm\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("VideoPreview_MID_vp9_webm: ");
            videoPreviewMidVp9WebmUrl = Console.ReadLine();
        }

        Console.Write("Video_HIGH_vp9_webm: ");
        string? videoHighVp9WebmUrl = Console.ReadLine();
        // If the url doesn't contain "video_HIGH.vp9.webm" ask if the user is sure, if not ask for the url again
        while (!videoHighVp9WebmUrl.Contains("video_HIGH.vp9.webm"))
        {
            Console.WriteLine("The url doesn't contain \"video_HIGH.vp9.webm\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("Video_HIGH_vp9_webm: ");
            videoHighVp9WebmUrl = Console.ReadLine();
        }

        Console.Write("Audio_opus: ");
        string? audioOpusUrl = Console.ReadLine();
        // If the url doesn't contain "audio.opus" ask if the user is sure, if not ask for the url again
        while (!audioOpusUrl.Contains("audio.opus"))
        {
            Console.WriteLine("The url doesn't contain \"audio.opus\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("Audio_opus: ");
            audioOpusUrl = Console.ReadLine();
        }

        Console.Write("MapPackage: ");
        string? mapPackageUrl = Console.ReadLine();
        // If the url doesn't contain "mapPackage" ask if the user is sure, if not ask for the url again
        while (!mapPackageUrl.Contains("mapPackage"))
        {
            Console.WriteLine("The url doesn't contain \"mapPackage\".");
            Console.Write("Are you sure this is the correct url? (y/n): ");
            string? answer = Console.ReadLine();
            if (answer == "y")
                break;
            Console.Write("MapPackage: ");
            mapPackageUrl = Console.ReadLine();
        }

        // Create the cache
        // Ask for a cache location
        string? path = null;
        while (string.IsNullOrWhiteSpace(path))
        {
            Console.Write("Enter the path to the cache file: ");
            path = Console.ReadLine();

            // If the path is empty, set it to null
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("The path is empty.");
                path = null;
                continue;
            }

            // If the path has quotes, remove them
            if (path.StartsWith('"') && path.EndsWith('"'))
                path = path[1..^1];

            // If the path is a file, set it to null
            if (File.Exists(path))
            {
                Console.WriteLine("The path is a file.");
                path = null;
                continue;
            }
        }

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
        string json = $$"""
            {
              "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
              "totalSize": 0,
              "free": 71568604,
              "journal": 0,
              "cachedStreamsDict": {
                "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
              },
              "name": "{{guid}}",
              "path": "/CacheStorage_{{cacheNumber}}/{{guid}}",
              "pathNX": "CacheStorage_{{cacheNumber}}/{{guid}}",
              "index": {{cacheNumber}}
            }
            """;
        File.WriteAllText(jsonCachePath, json);

        // Ask for the parentMapId
        Console.WriteLine("You are required to look for the parentMapId. You can find this on the wiki.");
        Console.Write("Enter the ParentMapId: ");
        string? parentMapId = Console.ReadLine();

        //// Open the webm file and get the duration
        //string webmPath = Path.Combine(cachexPath, "Video_HIGH_vp9_webm", videoHighVp9WebmUrl);
        //using MediaFoundationReader reader = new(webmPath);
        //double songDuration = reader.TotalTime.TotalSeconds;

        // Ask for the song duration
        Console.Write("Enter the song duration in seconds: ");
        double songDuration = double.Parse(Console.ReadLine()!);

        // Create a string JDSong dict
        Dictionary<string, JDSong> MapsDict;

        // Generate the JDSong
        JDSong song = new()
        {
            SongDatabaseEntry = new()
            {
                MapId = guid,
                ParentMapId = parentMapId,
                Title = songTitle,
                Artist = artistName,
                Credits = "Made possible by MrKev312",
                LyricsColor = "#FFFFFF",
                MapLength = songDuration,
                OriginalJDVersion = originalJustDanceVersion,
                CoachCount = coachCount,
                Difficulty = difficulty,
                SweatDifficulty = sweatDifficulty,
                Tags = ["Main"],
                TagIds = [],
                SearchTagsLocIds = [],
                CoachNamesLocIds = [],
                HasSongTitleInCover = false
            },
            AudioPreviewTrk = audioTrackData,
            AssetFilesDict = new()
            {
                Cover = new()
                {
                    AssetType = AssetType.Cover,
                    Name = "Cover",
                    Hash = coverUrl,
                    Ready = true,
                    Size = 0,
                    Category = 0,
                    FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/Cover/{coverUrl}"
                },
                CoachesSmall = new()
                {
                    AssetType = AssetType.CoachesSmall,
                    Name = "CoachesSmall",
                    Hash = coachesSmallUrl,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/{guid}/CoachesSmall/{coachesSmallUrl}"
                },
                CoachesLarge = new()
                {
                    AssetType = AssetType.CoachesLarge,
                    Name = "CoachesLarge",
                    Hash = coachesLargeUrl,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/{guid}/CoachesLarge/{coachesLargeUrl}"
                },
                AudioPreview_opus = new()
                {
                    AssetType = AssetType.AudioPreview_opus,
                    Name = "AudioPreview_opus",
                    Hash = audioPreviewOpusUrl,
                    Ready = true,
                    Size = 0,
                    Category = 0,
                    FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/AudioPreview_opus/{audioPreviewOpusUrl}"
                },
                VideoPreview_MID_vp9_webm = new()
                {
                    AssetType = AssetType.VideoPreview_MID_vp9_webm,
                    Name = "VideoPreview_MID_vp9_webm",
                    Hash = videoPreviewMidVp9WebmUrl,
                    Ready = true,
                    Size = 0,
                    Category = 0,
                    FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/VideoPreview_MID_vp9_webm/{videoPreviewMidVp9WebmUrl}"
                },
                Audio_opus = new()
                {
                    AssetType = AssetType.Audio_opus,
                    Name = "Audio_opus",
                    Hash = audioOpusUrl,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/{guid}/Audio_opus/{audioOpusUrl}"
                },
                Video_HIGH_vp9_webm = new()
                {
                    AssetType = AssetType.Video_HIGH_vp9_webm,
                    Name = "Video_HIGH_vp9_webm",
                    Hash = videoHighVp9WebmUrl,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/{guid}/Video_HIGH_vp9_webm/{videoHighVp9WebmUrl}"
                },
                MapPackage = new()
                {
                    AssetType = AssetType.MapPackage,
                    Name = "MapPackage",
                    Hash = mapPackageUrl,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/{guid}/MapPackage/{mapPackageUrl}"
                }
            },
            Sizes = new(),
            HasSongTitleInCover = hasSongTitleInCover ? true : null
        };

        // If the song has the song title in the cover, set the songTitleLogo
        if (hasSongTitleInCover)
        {
            song.AssetFilesDict.SongTitleLogo = new()
            {
                AssetType = AssetType.SongTitleLogo,
                Name = "Cover",
                Hash = songTitleLogoUrl,
                Ready = true,
                Size = 0,
                Category = 0,
                FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/Cover/{songTitleLogoUrl}"
            };
        }

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

    internal static void ConvertToProperCache()
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
        string cache0Json = $$"""
            {
              "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
              "totalSize": 0,
              "free": 71568604,
              "journal": 0,
              "cachedStreamsDict": {
                "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
              },
              "name": "MapBaseCache",
              "path": "/CacheStorage_0/MapBaseCache",
              "pathNX": "CacheStorage_0/MapBaseCache",
              "index": 0
            }
            """;
        File.WriteAllText(Path.Combine(cache0Path, "json.cache"), cache0Json);

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
            string json = $$"""
            {
              "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
              "totalSize": 0,
              "free": 71568604,
              "journal": 0,
              "cachedStreamsDict": {
                "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
              },
              "name": "{{song.Key}}",
              "path": "/CacheStorage_{{cacheNumber}}/{{song.Key}}",
              "pathNX": "CacheStorage_{{cacheNumber}}/{{song.Key}}",
              "index": {{cacheNumber}}
            }
            """;

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
        Console.WriteLine(mapping);
    }

    internal static void GenerateCacheWithExistingData()
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
        uint cacheNumber = (uint)Question.AskNumber("Enter the cache number: ", 1);

        // Create the cache_x and cache_0 folders
        string cacheXPath = Path.Combine(savePath, $"SD_Cache.{cacheNumber:4X}");
        string cache0Path = Path.Combine(savePath, "SD_Cache.0000");
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
            string jsonCache = $$"""
            {
              "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
              "totalSize": 0,
              "free": 71568604,
              "journal": 0,
              "cachedStreamsDict": {
                "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
              },
              "name": "{{map}}",
              "path": "/CacheStorage_{{cacheNumber}}/{{map}}",
              "pathNX": "CacheStorage_{{cacheNumber}}/{{map}}",
              "index": {{cacheNumber}}
            }
            """;
            File.WriteAllText(jsonCachePath, jsonCache);

            JDNextUbiMapData songData = json[map];

            // Generate the JDSong
            JDSong song = new()
            {
                SongDatabaseEntry = new()
                {
                    MapId = map,
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
                    TagIds = [.. songData.tagIds],
                    SearchTagsLocIds = [],
                    CoachNamesLocIds = [.. songData.coachNamesLocIds],
                    HasSongTitleInCover = false
                },
                AudioPreviewTrk = audioTrackData,
                AssetFilesDict = new()
                {
                    Cover = new()
                    {
                        AssetType = AssetType.Cover,
                        Name = "Cover",
                        Hash = coverHash,
                        Ready = true,
                        Size = 0,
                        Category = 0,
                        FilePath = $"/CacheStorage_0/MapBaseCache/{map}/Cover/{coverHash}"
                    },
                    CoachesSmall = new()
                    {
                        AssetType = AssetType.CoachesSmall,
                        Name = "CoachesSmall",
                        Hash = coachesSmallHash,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{map}/CoachesSmall/{coachesSmallHash}"
                    },
                    CoachesLarge = new()
                    {
                        AssetType = AssetType.CoachesLarge,
                        Name = "CoachesLarge",
                        Hash = coachesLargeHash,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{map}/CoachesLarge/{coachesLargeHash}"
                    },
                    AudioPreview_opus = new()
                    {
                        AssetType = AssetType.AudioPreview_opus,
                        Name = "AudioPreview_opus",
                        Hash = audioPreviewOpusHash,
                        Ready = true,
                        Size = 0,
                        Category = 0,
                        FilePath = $"/CacheStorage_0/MapBaseCache/{map}/AudioPreview_opus/{audioPreviewOpusHash}"
                    },
                    VideoPreview_MID_vp9_webm = new()
                    {
                        AssetType = AssetType.VideoPreview_MID_vp9_webm,
                        Name = "VideoPreview_MID_vp9_webm",
                        Hash = videoPreviewMidVp9WebmHash,
                        Ready = true,
                        Size = 0,
                        Category = 0,
                        FilePath = $"/CacheStorage_0/MapBaseCache/{map}/VideoPreview_MID_vp9_webm/{videoPreviewMidVp9WebmHash}"
                    },
                    Audio_opus = new()
                    {
                        AssetType = AssetType.Audio_opus,
                        Name = "Audio_opus",
                        Hash = audioOpusHash,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{map}/Audio_opus/{audioOpusHash}"
                    },
                    Video_HIGH_vp9_webm = new()
                    {
                        AssetType = AssetType.Video_HIGH_vp9_webm,
                        Name = "Video_HIGH_vp9_webm",
                        Hash = videoHighVp9WebmHash,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{map}/Video_HIGH_vp9_webm/{videoHighVp9WebmHash}"
                    },
                    MapPackage = new()
                    {
                        AssetType = AssetType.MapPackage,
                        Name = "MapPackage",
                        Hash = mapPackageHash,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{map}/MapPackage/{mapPackageHash}"
                    }
                },
                Sizes = new(),
                HasSongTitleInCover = hasSongTitleLogo ? true : null
            };

            // If the song has the song title in the cover, set the songTitleLogo
            if (hasSongTitleLogo)
            {
                song.AssetFilesDict.SongTitleLogo = new()
                {
                    AssetType = AssetType.SongTitleLogo,
                    Name = "Cover",
                    Hash = songTitleLogoHash,
                    Ready = true,
                    Size = 0,
                    Category = 0,
                    FilePath = $"/CacheStorage_0/MapBaseCache/{map}/Cover/{songTitleLogoHash}"
                };
            }

            // Add the song to the dict
            MapsDict.Add(map, song);
        }

        // Convert the dict to json and save it
        string jsonCache0 = JsonSerializer.Serialize(new JDCacheJSON
        {
            MapsDict = MapsDict
        }, JsonSerializerOptions);

        File.WriteAllText(Path.Combine(cache0Path, "json.cache"), jsonCache0);

        Console.WriteLine(jsonCache0);

        Console.WriteLine();
        Console.WriteLine("Done.");
    }

    internal static void GenerateCacheWithSongDBAndUrls()
    {
        // First off, where do we want to save the cache?
        string? path = Question.AskFolder("Enter the path for where to save the cache: ", false);
        Directory.CreateDirectory(path);

        // Ask for the cache number
        Console.Write("Enter the cache number: ");
        uint? cacheNumber = null;
        while (cacheNumber == null)
        {
            string? cacheNumberString = Console.ReadLine();

            // If the cache number is not a number, print an error and continue
            if (!uint.TryParse(cacheNumberString, out uint value))
            {
                Console.WriteLine("The cache number is not a number.");
                continue;
            }

            // If the cache number is not valid, print an error and continue
            if (value < 0)
            {
                Console.WriteLine("The cache number must be 1 or higher.");
                continue;
            }

            // The number can't be 0
            if (value == 0)
            {
                Console.WriteLine("The cache number can't be 0.");
                continue;
            }

            cacheNumber = value;
        }

        // First get the songDB
        string songDBPath = Question.AskFile("Enter the songDB path: ", true);

        // Ask for a secondary songDB, can be empty
        string? songDBSecondaryPath = Question.AskFile("Enter the secondary songDB path, or leave empty: ", false);

        // Read the songDB
        string songDBJson = File.ReadAllText(songDBPath);

        // Parse the songDB
        Dictionary<string, SongData> songDB = JsonSerializer.Deserialize<Dictionary<string, SongData>>(songDBJson)!;

        // If there's a secondary songDB, read it and parse it, merge it with the songDB but overwrite existing entries
        if (!string.IsNullOrEmpty(songDBSecondaryPath))
        {
            string songDBSecondaryJson = File.ReadAllText(songDBSecondaryPath);
            Dictionary<string, SongData> songDBSecondary = JsonSerializer.Deserialize<Dictionary<string, SongData>>(songDBSecondaryJson)!;

            foreach (KeyValuePair<string, SongData> songData in songDBSecondary)
                // If the songDB doesn't contain the song, add it
                if (!songDB.ContainsKey(songData.Key))
                    songDB.Add(songData.Key, songData.Value);
        }


        Dictionary<string, JDSong> MapsDict = [];

        while (true)
        {
            SongData? songData = null;

            while (songData == null)
            {
                // Ask for the song title
                Console.Write("Enter the song title (or exit): ");
                string? songTitle = Console.ReadLine();

                if (songTitle is "exit" or "")
                    break;

                // Try to find all songs with the song title, either containing or equal to
                List<SongData> songs = songDB.Values.Where(x => x.title.Contains(songTitle, StringComparison.OrdinalIgnoreCase) || x.title.Equals(songTitle, StringComparison.OrdinalIgnoreCase)).ToList();

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

                    uint? choice = null;

                    while (choice == null)
                    {
                        // Ask for the song number
                        Console.Write("Enter the song number: ");

                        string? songNumber = Console.ReadLine();

                        // If the song number is not a number, print an error and continue
                        if (!uint.TryParse(songNumber, out uint value))
                        {
                            Console.WriteLine("The song number is not a number.");
                            continue;
                        }

                        // If the song number is not valid, print an error and continue
                        if (value < 0 || value > songs.Count)
                        {
                            Console.WriteLine("The song number is not valid.");
                            continue;
                        }

                        choice = value;
                    }

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
            Console.Write("CoachesSmall: ");
            string? coachesSmallUrl = Console.ReadLine();
            // If the url doesn't contain "coachesSmall" ask if the user is sure, if not ask for the url again
            while (!coachesSmallUrl.Contains("coachesSmall"))
            {
                Console.WriteLine("The url doesn't contain \"coachesSmall\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("CoachesSmall: ");
                coachesSmallUrl = Console.ReadLine();
            }

            Console.Write("CoachesLarge: ");
            string? coachesLargeUrl = Console.ReadLine();
            // If the url doesn't contain "coachesLarge" ask if the user is sure, if not ask for the url again
            while (!coachesLargeUrl.Contains("coachesLarge"))
            {
                Console.WriteLine("The url doesn't contain \"coachesLarge\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("CoachesLarge: ");
                coachesLargeUrl = Console.ReadLine();
            }

            Console.Write("Cover: ");
            string? coverUrl = Console.ReadLine();
            // If the url doesn't contain "cover" ask if the user is sure, if not ask for the url again
            while (!coverUrl.Contains("cover"))
            {
                Console.WriteLine("The url doesn't contain \"cover\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("Cover: ");
                coverUrl = Console.ReadLine();
            }

            string? songTitleLogoUrl = "";
            Console.Write("songTitleLogo (can be empty): ");
            songTitleLogoUrl = Console.ReadLine();
            // If the url isn't empty and doesn't contain "songTitleLogo" ask if the user is sure, if not ask for the url again
            while (!string.IsNullOrEmpty(songTitleLogoUrl) && !songTitleLogoUrl.Contains("songTitleLogo"))
            {
                Console.WriteLine("The url doesn't contain \"songTitleLogo\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("songTitleLogo (can be empty): ");
                songTitleLogoUrl = Console.ReadLine();
            }

            bool hasSongTitleInCover = !string.IsNullOrEmpty(songTitleLogoUrl);

            Console.Write("AudioPreview_opus: ");
            string? audioPreviewOpusUrl = Console.ReadLine();
            // If the url doesn't contain "audioPreview.opus" ask if the user is sure, if not ask for the url again
            while (!audioPreviewOpusUrl.Contains("audioPreview.opus"))
            {
                Console.WriteLine("The url doesn't contain \"audioPreview.opus\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("AudioPreview_opus: ");
                audioPreviewOpusUrl = Console.ReadLine();
            }

            Console.Write("VideoPreview_MID_vp9_webm: ");
            string? videoPreviewMidVp9WebmUrl = Console.ReadLine();
            // If the url doesn't contain "videoPreview_MID.vp9.webm" ask if the user is sure, if not ask for the url again
            while (!videoPreviewMidVp9WebmUrl.Contains("videoPreview_MID.vp9.webm"))
            {
                Console.WriteLine("The url doesn't contain \"videoPreview_MID.vp9.webm\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("VideoPreview_MID_vp9_webm: ");
                videoPreviewMidVp9WebmUrl = Console.ReadLine();
            }

            Console.Write("Video_HIGH_vp9_webm: ");
            string? videoHighVp9WebmUrl = Console.ReadLine();
            // If the url doesn't contain "video_HIGH.vp9.webm" ask if the user is sure, if not ask for the url again
            while (!videoHighVp9WebmUrl.Contains("video_HIGH.vp9.webm"))
            {
                Console.WriteLine("The url doesn't contain \"video_HIGH.vp9.webm\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("Video_HIGH_vp9_webm: ");
                videoHighVp9WebmUrl = Console.ReadLine();
            }

            Console.Write("Audio_opus: ");
            string? audioOpusUrl = Console.ReadLine();
            // If the url doesn't contain "audio.opus" ask if the user is sure, if not ask for the url again
            while (!audioOpusUrl.Contains("audio.opus"))
            {
                Console.WriteLine("The url doesn't contain \"audio.opus\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("Audio_opus: ");
                audioOpusUrl = Console.ReadLine();
            }

            Console.Write("MapPackage: ");
            string? mapPackageUrl = Console.ReadLine();
            // If the url doesn't contain "mapPackage" ask if the user is sure, if not ask for the url again
            while (!mapPackageUrl.Contains("mapPackage"))
            {
                Console.WriteLine("The url doesn't contain \"mapPackage\".");
                Console.Write("Are you sure this is the correct url? (y/n): ");
                string? answer = Console.ReadLine();
                if (answer == "y")
                    break;
                Console.Write("MapPackage: ");
                mapPackageUrl = Console.ReadLine();
            }

            // Turned out to not be unique, which makes things a bit harder to prevent collisions
            //// We look at the mapPackageUrl, for no reason other than that it's the last one, for the GUID
            //// Create a URI and 2nd part of the URI in one line
            //string? guid = new Uri(mapPackageUrl).Segments[1];

            //// If the guid ends with a /, remove it
            //if (guid.EndsWith('/'))
            //    guid = guid[..^1];

            //// If this isn't a valid GUID, ask for a GUID type 4, or whether to generate one
            //if (!Guid.TryParse(guid, out _))
            //{
            //    Console.WriteLine("The automatically scraped GUID is not valid.");
            //    Console.Write("Enter the GUID, or leave empty to generate one: ");
            //    guid = Console.ReadLine();
            //    if (string.IsNullOrEmpty(guid))
            //        guid = Guid.NewGuid().ToString();
            //}
            //else
            //{
            //    Console.WriteLine($"The automatically scraped GUID is valid: {guid}");
            //}

            // Generate a GUID
            string guid = Guid.NewGuid().ToString();

            //// This didn't work so we'll use a hardcoded value, really don't know how this format works
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
            string cachexPath = Path.Combine(path, $"SD_Cache.{cacheNumber.Value:X4}", guid);
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
            string json = $$"""
            {
              "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
              "totalSize": 0,
              "free": 71568604,
              "journal": 0,
              "cachedStreamsDict": {
                "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
              },
              "name": "{{guid}}",
              "path": "/CacheStorage_{{cacheNumber}}/{{guid}}",
              "pathNX": "CacheStorage_{{cacheNumber}}/{{guid}}",
              "index": {{cacheNumber}}
            }
            """;

            // Write the json to the file
            File.WriteAllText(jsonCachePath, json);

            // Generate the JDSong
            JDSong song = new()
            {
                SongDatabaseEntry = new()
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
                    HasSongTitleInCover = false
                },
                AudioPreviewTrk = audioTrackData,
                AssetFilesDict = new()
                {
                    Cover = new()
                    {
                        AssetType = AssetType.Cover,
                        Name = "Cover",
                        Hash = coverUrl,
                        Ready = true,
                        Size = 0,
                        Category = 0,
                        FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/Cover/{coverUrl}"
                    },
                    CoachesSmall = new()
                    {
                        AssetType = AssetType.CoachesSmall,
                        Name = "CoachesSmall",
                        Hash = coachesSmallUrl,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{guid}/CoachesSmall/{coachesSmallUrl}"
                    },
                    CoachesLarge = new()
                    {
                        AssetType = AssetType.CoachesLarge,
                        Name = "CoachesLarge",
                        Hash = coachesLargeUrl,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{guid}/CoachesLarge/{coachesLargeUrl}"
                    },
                    AudioPreview_opus = new()
                    {
                        AssetType = AssetType.AudioPreview_opus,
                        Name = "AudioPreview_opus",
                        Hash = audioPreviewOpusUrl,
                        Ready = true,
                        Size = 0,
                        Category = 0,
                        FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/AudioPreview_opus/{audioPreviewOpusUrl}"
                    },
                    VideoPreview_MID_vp9_webm = new()
                    {
                        AssetType = AssetType.VideoPreview_MID_vp9_webm,
                        Name = "VideoPreview_MID_vp9_webm",
                        Hash = videoPreviewMidVp9WebmUrl,
                        Ready = true,
                        Size = 0,
                        Category = 0,
                        FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/VideoPreview_MID_vp9_webm/{videoPreviewMidVp9WebmUrl}"
                    },
                    Audio_opus = new()
                    {
                        AssetType = AssetType.Audio_opus,
                        Name = "Audio_opus",
                        Hash = audioOpusUrl,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{guid}/Audio_opus/{audioOpusUrl}"
                    },
                    Video_HIGH_vp9_webm = new()
                    {
                        AssetType = AssetType.Video_HIGH_vp9_webm,
                        Name = "Video_HIGH_vp9_webm",
                        Hash = videoHighVp9WebmUrl,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{guid}/Video_HIGH_vp9_webm/{videoHighVp9WebmUrl}"
                    },
                    MapPackage = new()
                    {
                        AssetType = AssetType.MapPackage,
                        Name = "MapPackage",
                        Hash = mapPackageUrl,
                        Ready = true,
                        Size = 0,
                        Category = 1,
                        FilePath = $"/CacheStorage_{cacheNumber}/{guid}/MapPackage/{mapPackageUrl}"
                    }
                },
                Sizes = new(),
                HasSongTitleInCover = hasSongTitleInCover ? true : null
            };

            // If the song has the song title in the cover, set the songTitleLogo
            if (hasSongTitleInCover)
            {
                song.AssetFilesDict.SongTitleLogo = new()
                {
                    AssetType = AssetType.SongTitleLogo,
                    Name = "Cover",
                    Hash = songTitleLogoUrl,
                    Ready = true,
                    Size = 0,
                    Category = 0,
                    FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/Cover/{songTitleLogoUrl}"
                };
            }

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
