using System.Text.Encodings.Web;
using System.Text.Json;

using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

namespace JustDanceEditor.Converter.Converters.Cache;

public static class CacheJsonGenerator
{
    static readonly JsonSerializerOptions options = new() { 
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static bool MergeCaches(ConvertUbiArtToUnity convert)
    {
        try
        {
            MergeCachesInternal(convert);
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to merge cache json files: {e.Message}", LogLevel.Error);
        }

        return false;
    }

    static void MergeCachesInternal(ConvertUbiArtToUnity convert)
    {
        // First we load the generated JSON
        string cachingStatusPath = Path.Combine(convert.FileSystem.OutputFolders.OutputFolder, "cachingStatus.json");
        Dictionary<string, JDSong> caching = JsonSerializer.Deserialize<Dictionary<string, JDSong>>(File.ReadAllText(cachingStatusPath), options)!;

        // The one we'll add to is in the existing SD_0000 folder
        string cachingStatusPath0 = convert.FileSystem.OutputFolders.CachingStatusPath;
        JDCacheJSON caching0 = JsonSerializer.Deserialize<JDCacheJSON>(File.ReadAllText(cachingStatusPath0), options)!;

        // Merge the two dictionaries
        if (caching0.MapsDict.ContainsKey(convert.SongID))
        {
            throw new Exception("Song already exists in the cache");
        }

        caching0.MapsDict.Add(convert.SongID, caching[convert.SongID]);

        // Now we move the files first and after that we write the new cachingStatus.json
        // This is in case we crash while moving the files, we don't want to have the cache.json updated without the files
        // Moving the SD_Cache.0000 folder
        string sd0000Path = convert.FileSystem.OutputFolders.PreviewFolder;
        string sd0000PathDest = Path.Combine(convert.ConversionRequest.OutputPath, "SD_Cache.0000", "MapBaseCache", convert.SongID);
        Directory.Move(sd0000Path, sd0000PathDest);

        // Moving the SD_Cache.xxxx folder
        string sdXFolder = convert.FileSystem.OutputFolders.MapFolder;
        uint cacheNumber = convert.FileSystem.OutputFolders.CacheNumber;
        string sdXFolderDest = Path.Combine(convert.ConversionRequest.OutputPath, $"SD_Cache.{cacheNumber:X4}", convert.SongID);
        Directory.CreateDirectory(Path.Combine(convert.ConversionRequest.OutputPath, $"SD_Cache.{cacheNumber:X4}"));
        Directory.Move(sdXFolder, sdXFolderDest);

        // Write the new cachingStatus.json
        string cachingStatus = JsonSerializer.Serialize(caching0, options);
        File.WriteAllText(cachingStatusPath0, cachingStatus);

        // Delete the old cachingStatus.json
        File.Delete(cachingStatusPath);

        // Recursively remove empty directories
        string outputFolder = convert.FileSystem.OutputFolders.OutputFolder;
        RecursivelyRemoveEmptyDirectories(outputFolder);

        // If the output folder is empty, remove it
        if (Directory.GetFiles(outputFolder).Length == 0 && Directory.GetDirectories(outputFolder).Length == 0)
        {
            Directory.Delete(outputFolder);
        }
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

    public static void GenerateCacheJson(ConvertUbiArtToUnity convert)
    {
        try
        {
            GenerateCacheJsonInternal(convert);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate cache json, usually means something went wrong before: {e.Message}", LogLevel.Error);
        }
    }

    static void GenerateCacheJsonInternal(ConvertUbiArtToUnity convert)
    {
        OutputFolders outputFolders = convert.FileSystem.OutputFolders;

        // Generate the json.cache file
        string cachexJsonPath = Path.Combine(outputFolders.MapFolder, "json.cache");
        uint cacheNumber = outputFolders.CacheNumber;
        string cachexJson = JDSongFactory.CacheJson(cacheNumber, convert.SongID);
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

        string cachingStatusPath = Path.Combine(outputFolders.OutputFolder, "cachingStatus.json");
        JDSong jdSong = JDSongFactory.CreateSong((SongDatabaseEntry)convert, cacheNumber, coverName, coachesSmallName, coachesLargeName, audioPreviewName, videoPreviewName, audioName, videoName, mapPackageName, songTitleLogoName, convert.SongID);

        Dictionary<string, JDSong> caching = new()
        {
            { convert.SongID, jdSong }
        };

        // Generate the cachingStatus.json file
        string cachingStatus = JsonSerializer.Serialize(caching, options);

        File.WriteAllText(cachingStatusPath, cachingStatus);
    }
}
