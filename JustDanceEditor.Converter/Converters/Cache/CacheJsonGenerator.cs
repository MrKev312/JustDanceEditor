using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using System.Text.Encodings.Web;
using System.Text.Json;

namespace JustDanceEditor.Converter.Converters.Cache;

public static class CacheJsonGenerator
{
    static readonly JsonSerializerOptions options = new() { 
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    static readonly JsonSerializerOptions optionsCamelCase = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static bool MergeCaches(ConversionContext context)
    {
        try
        {
            MergeCachesInternal(context);
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to merge cache json files: {e.Message}", LogLevel.Error);
        }

        return false;
    }

    static void MergeCachesInternal(ConversionContext context)
    {
        // First we load the generated JSON
        string cachingStatusPath = context.FileSystem.OutputFolders.CachePath;
        Dictionary<Guid, JDSong> caching = JsonSerializer.Deserialize<Dictionary<Guid, JDSong>>(File.ReadAllText(cachingStatusPath), options)!;

        // The one we'll add to is in the existing SD_0000 folder
        string cachingStatusPath0 = context.FileSystem.OutputFolders.CachingStatusPath;
        JDCacheJSON caching0 = JsonSerializer.Deserialize<JDCacheJSON>(File.ReadAllText(cachingStatusPath0), options)!;

        // Merge the two dictionaries
        if (caching0.MapsDict.ContainsKey(context.SongID))
        {
            throw new Exception("Song already exists in the cache");
        }

        caching0.MapsDict.Add(context.SongID, caching[context.SongID]);

        // Now we move the files first and after that we write the new cachingStatus.json
        // This is in case we crash while moving the files, we don't want to have the cache.json updated without the files
        // Moving the SD_Cache.0000 folder
        string sd0000Path = context.FileSystem.OutputFolders.PreviewFolder;
        string sd0000PathDest = Path.Combine(context.Request.OutputPath, "SD_Cache.0000", "MapBaseCache", context.SongID.ToString());
        Directory.Move(sd0000Path, sd0000PathDest);

        // Moving the SD_Cache.xxxx folder
        string sdXFolder = context.FileSystem.OutputFolders.MapFolder;
        uint cacheNumber = context.FileSystem.OutputFolders.CacheNumber;
        string sdXFolderDest = Path.Combine(context.Request.OutputPath, $"SD_Cache.{cacheNumber:X4}", context.SongID.ToString());
        Directory.CreateDirectory(Path.Combine(context.Request.OutputPath, $"SD_Cache.{cacheNumber:X4}"));
        Directory.Move(sdXFolder, sdXFolderDest);

        // Write the new cachingStatus.json
        string cachingStatus = JsonSerializer.Serialize(caching0, options);
        File.WriteAllText(cachingStatusPath0, cachingStatus);

        // Delete the old cachingStatus.json
        File.Delete(cachingStatusPath);

        // Recursively remove empty directories
        string outputFolder = context.FileSystem.OutputFolders.OutputFolder;
        RecursivelyRemoveEmptyDirectories(outputFolder);

        // If the output folder is empty, remove it
        if (Directory.GetFiles(outputFolder).Length == 0 && Directory.GetDirectories(outputFolder).Length == 0)
            Directory.Delete(outputFolder);
    }

    static void RecursivelyRemoveEmptyDirectories(string path)
    {
        foreach (string directory in Directory.GetDirectories(path))
        {
            RecursivelyRemoveEmptyDirectories(directory);
            if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
    }

    public static void GenerateCacheJson(ConversionContext context)
    {
        try
        {
            GenerateCacheJsonInternal(context);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate cache json, usually means something went wrong before: {e.Message}", LogLevel.Error);
        }
    }

    static void GenerateCacheJsonInternal(ConversionContext context)
    {
        if (context.Request.ExportType == ExportType.OfflineCache)
        {
            GenerateOfflineCacheJson(context);
        }
        else
        {
            GenerateServerCacheJson(context);
        }
    }

    static void GenerateOfflineCacheJson(ConversionContext context)
    {
        OutputFolders outputFolders = context.FileSystem.OutputFolders;

        // Generate the json.cache file
        string cachexJsonPath = Path.Combine(outputFolders.MapFolder, "json.cache");
        uint cacheNumber = outputFolders.CacheNumber;
        string cachexJson = JDSongFactory.CacheJson(cacheNumber, context.SongID);
        File.WriteAllText(cachexJsonPath, cachexJson);

        string audioName = Path.GetFileName(Directory.GetFiles(outputFolders.AudioFolder)[0]);
        string audioPreviewName = Path.GetFileName(Directory.GetFiles(outputFolders.PreviewAudioFolder)[0]);
        string coverName = Path.GetFileName(Directory.GetFiles(outputFolders.CoverFolder)[0]);
        string coachesLargeName = Path.GetFileName(Directory.GetFiles(outputFolders.CoachesLargeFolder)[0]);
        string coachesSmallName = Path.GetFileName(Directory.GetFiles(outputFolders.CoachesSmallFolder)[0]);
        string mapPackageName = Path.GetFileName(Directory.GetFiles(outputFolders.MapPackageFolder)[0]);
        string videoName = Path.GetFileName(Directory.GetFiles(outputFolders.VideoFolder)[0]);
        string videoPreviewName = Path.GetFileName(Directory.GetFiles(outputFolders.PreviewVideoFolder)[0]);

        string? songTitleLogoName = null;
        if (Directory.Exists(outputFolders.SongTitleLogoFolder))
        {
            songTitleLogoName = Path.GetFileName(Directory.GetFiles(outputFolders.SongTitleLogoFolder)[0]);
        }

        string cachingStatusPath = context.FileSystem.OutputFolders.CachePath;
        JDSong jdSong = JDSongFactory.CreateSong((SongDatabaseEntry)context, cacheNumber, coverName, coachesSmallName, coachesLargeName, audioPreviewName, videoPreviewName, audioName, videoName, mapPackageName, songTitleLogoName, context.SongID);

        Dictionary<Guid, JDSong> caching = new()
        {
            { context.SongID, jdSong }
        };

        // Generate the cachingStatus.json file
        string cachingStatus = JsonSerializer.Serialize(caching, options);

        File.WriteAllText(cachingStatusPath, cachingStatus);
    }

    static void GenerateServerCacheJson(ConversionContext context)
    {
        OutputFolders outputFolders = context.FileSystem.OutputFolders;

        string cachingStatusPath = context.FileSystem.OutputFolders.CachePath;

        // Convert the songdatabase
        ServerSongJSON serverSong = (ServerSongJSON)context;
        string serverSongJSON = JsonSerializer.Serialize(serverSong, optionsCamelCase);

        File.WriteAllText(cachingStatusPath, serverSongJSON);
    }
}
