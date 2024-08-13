using System.Text.Encodings.Web;
using System.Text.Json;

using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

namespace JustDanceEditor.Converter.Converters.Cache;

public static class CacheJsonGenerator
{
    static readonly JsonSerializerOptions options = new() { 
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

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
        // Generate the json.cache file
        string cachexJsonPath = Path.Combine(convert.OutputXFolder, "json.cache");
        string cachexJson = JDSongFactory.CacheJson(convert.CacheNumber, convert.SongID);
        File.WriteAllText(cachexJsonPath, cachexJson);

        string audioName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.OutputXFolder, "Audio_opus"))[0]);
        string audioPreviewName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.Output0Folder, "AudioPreview_opus"))[0]);
        string coverName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.Output0Folder, "Cover"))[0]);
        string coachesSmallName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.OutputXFolder, "CoachesSmall"))[0]);
        string coachesLargeName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.OutputXFolder, "CoachesLarge"))[0]);
        string mapPackageName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.OutputXFolder, "MapPackage"))[0]);
        string videoName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.OutputXFolder, "Video_HIGH_vp9_webm"))[0]);
        string videoPreviewName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.Output0Folder, "VideoPreview_MID_vp9_webm"))[0]);

        string? songTitleLogoName = null;
        if (Directory.Exists(Path.Combine(convert.Output0Folder, "songTitleLogo")))
        {
            songTitleLogoName = Path.GetFileName(Directory.GetFiles(Path.Combine(convert.Output0Folder, "songTitleLogo"))[0]);
        }

        string cachingStatusPath = Path.Combine(convert.OutputFolder, "cachingStatus.json");
        JDSong jdSong = JDSongFactory.CreateSong((SongDatabaseEntry)convert, convert.CacheNumber, coverName, coachesSmallName, coachesLargeName, audioPreviewName, videoPreviewName, audioName, videoName, mapPackageName, songTitleLogoName, convert.SongID);

        Dictionary<string, JDSong> caching = new()
        {
            { convert.SongID, jdSong }
        };

        // Generate the cachingStatus.json file
        string cachingStatus = JsonSerializer.Serialize(caching, options);
        // Remove the curly braces from the json
        cachingStatus = cachingStatus.TrimStart('{').TrimEnd('}');

        File.WriteAllText(cachingStatusPath, cachingStatus);
    }
}
