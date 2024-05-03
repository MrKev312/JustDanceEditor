using System.Reflection;

using JustDanceEditor.UI.Helpers;

namespace JustDanceEditor.UI;

internal class Program
{
    static void Main()
    {
        Console.WriteLine("Just Dance Editor");
        Console.WriteLine("Made by: MrKev312");
        Console.WriteLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version}");
        MainLoop();
        Console.WriteLine("Exiting...");
    }

    private static void MainLoop()
    {
        while (true)
        {
            int choice = Question.Ask([
                "Exit",
                "Cache stuff",
                "Convert UbiArt to Unity"
            ]);

            switch (choice)
            {
                case 0:
                    return;
                case 1:
                    CacheStuff();
                    break;
                case 2:
                    ConvertUbiArtToUnity.Convert();
                    break;
            }
        }
    }

    private static void CacheStuff()
    {
        //int choice = Question.Ask([
        //    "Exit",
        //    "Print cache information",
        //    "Cache generation stuff",
        //    "See which folders differ from the cache"
        //]);

        //switch (choice)
        //{

        //    case 0:
        //        return;
        //    case 1:
        //        Print.PrintCache();
        //        break;
        //    case 2:
        //        GenerateCache();
        //        break;
        //    case 3:
        //        Print.PrintCacheDifferences();
        //        break;
        //}
        Console.WriteLine("This part is currently not implemented.");

        //static void GenerateCache()
        //{
        //    // Ask if the user wants to generate a song with existing data or URLs
        //    int choice = Question.Ask([
        //        "Exit",
        //    "Generate a song with URLs (manual data)",
        //    "Generate a song with URLs + songdb",
        //    "Generate a song with existing data (jd-s3.cdn.ubi.com + json)",
        //    "Download all songs from songData.json + nextDB.json to Cache0/x",
        //    "Convert Cache0/x to proper cache"
        //    ]);

        //    switch (choice)
        //    {
        //        case 0:
        //            return;
        //        case 1:
        //            GenerateCacheURL.GenerateCacheWithUrls();
        //            break;
        //        case 2:
        //            GenerateCacheURL.GenerateCacheWithSongDBAndUrls();
        //            break;
        //        case 3:
        //            GenerateCacheURL.GenerateCacheWithExistingData();
        //            break;
        //        case 4:
        //            DownloadCache.DownloadAllSongs();
        //            break;
        //        case 5:
        //            GenerateCacheURL.ConvertToProperCache();
        //            break;
        //    }
        //}
    }


}