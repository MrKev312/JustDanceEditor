using JustDanceEditor.Converter.Unity;

using System.Text.Json;

namespace JustDanceEditor.UI.Helpers;
public class DownloadCache
{
    public static void DownloadAllSongs()
    {
        // First off, where do we want to save the cache?
        string? path = Question.AskFolder("Enter the path for where to save the cache: ", false);
        Directory.CreateDirectory(path);

        // Ask for the location of the songData.json file
        string songDataPath = Question.AskFile("Enter the songData.json file: ", true);
        Dictionary<string, SongData> songDatas = JsonSerializer.Deserialize<Dictionary<string, SongData>>(File.ReadAllText(songDataPath))!;

        // Ask for the nextDB.json file
        string nextDBPath = Question.AskFile("Enter the nextDB.json file: ", true);
        Dictionary<string, SongDBSongData> nextDB = JsonSerializer.Deserialize<Dictionary<string, SongDBSongData>>(File.ReadAllText(nextDBPath))!;

        // Ask which quality to download the preview and the content in
        int prevQuality = Question.Ask(["Low", "Mid", "High", "Ultra"], question: "Which quality do you want to download the preview in?");
        bool vp9Prev = Question.Ask(["VP8", "VP9"], question: "Do you want to download the preview in VP9?") == 1;
        int contQuality = Question.Ask(["Low", "Mid", "High", "Ultra"], question: "Which quality do you want to download the content in?");
        bool vp9Cont = Question.Ask(["VP8", "VP9"], question: "Do you want to download the content in VP9?") == 1;

        // Now we can loop through the songData and download the previews and the content
        foreach (KeyValuePair<string, SongData> song in songDatas)
        {
            // Get the song ID
            string songID = song.Key;

            // Get the song data
            SongData songData = song.Value;

            // Get the nextDB data
            SongDBSongData nextDBData = nextDB[songID];

            // Get the preview URL
            string previewURL = prevQuality switch
            {
                0 => vp9Prev ? songData.PreviewURLs.LOWvp9 : songData.PreviewURLs.LOWvp8,
                1 => vp9Prev ? songData.PreviewURLs.MIDvp9 : songData.PreviewURLs.MIDvp8,
                2 => vp9Prev ? songData.PreviewURLs.HIGHvp9 : songData.PreviewURLs.HIGHvp8,
                3 => vp9Prev ? songData.PreviewURLs.ULTRAvp9 : songData.PreviewURLs.ULTRAvp8,
                _ => throw new NotImplementedException()
            };

            // Get the content URL
            string contentURL = contQuality switch
            {
                0 => vp9Cont ? songData.ContentURLs.Lowvp9 : songData.ContentURLs.LowHD,
                1 => vp9Cont ? songData.ContentURLs.Midvp9 : songData.ContentURLs.MidHD,
                2 => vp9Cont ? songData.ContentURLs.Highvp9 : songData.ContentURLs.HighHD,
                3 => vp9Cont ? songData.ContentURLs.Ultravp9 : songData.ContentURLs.UltraHD,
                _ => throw new NotImplementedException()
            };

            // Create the folder for the song
            string cache0 = Path.Combine(path, "Cache0", songID);
            Directory.CreateDirectory(cache0);
            string cacheX = Path.Combine(path, "CacheX", songID);
            Directory.CreateDirectory(cacheX);
        }
    }
}
