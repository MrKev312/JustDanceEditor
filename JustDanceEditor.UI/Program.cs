using System.Reflection;

using JustDanceEditor.UI.Helpers;
using JustDanceEditor.UI.Converting;

namespace JustDanceEditor.UI;

internal class Program
{
    static void Main()
    {
        Console.WriteLine("Just Dance Editor");
        Console.WriteLine("Made by: MrKev312");
        Console.WriteLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version}");
        Console.WriteLine($"Current Directory: {Environment.CurrentDirectory}");

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
                "Convert UbiArt to Unity",
                "Extract IPK file"
            ]);

            switch (choice)
            {
                case 0:
                    return;
                case 1:
                    CacheStuff();
                    break;
                case 2:
                    ConverterDialogue.ConvertDialogue();
                    break;
                case 3:
                    ExtractorDialogue.ExtractDialogue();
                    break;
            }
        }
    }

    [Obsolete]
    static void CacheStuff()
    {
        Console.WriteLine("""
            Please note that this was made for some quick and dirty cache testing. This is not intended to be used by the end user.
            I will not give support nor explain how to use this. If you want to use this, you are on your own.

            Use at your own risk.

            """);

        int choice = Question.Ask([
            "Go back",
            "Print cache information",
            "Cache generation stuff",
            "See which folders differ from the cache"
        ]);

        switch (choice)
        {

            case 0:
                return;
            case 1:
                Print.PrintCache();
                break;
            case 2:
                GenerateCache();
                break;
            case 3:
                Print.PrintCacheDifferences();
                break;
        }

        [Obsolete]
        static void GenerateCache()
        {
            // Ask if the user wants to generate a song with existing data or URLs
            int choice = Question.Ask([
                "Go back",
                "Generate a song with URLs (manual data)",
                "Generate a song with URLs + songdb",
                "Generate a song with existing data (jd-s3.cdn.ubi.com + json)",
                "Download all songs from songData.json + nextDB.json to Cache0/x",
                "Convert Cache0/x to proper cache"
            ]);

            switch (choice)
            {
                case 0:
                    return;
                case 1:
                    GenerateCacheURL.GenerateCacheWithUrls();
                    break;
                case 2:
                    GenerateCacheURL.GenerateCacheWithSongDBAndUrls();
                    break;
                case 3:
                    GenerateCacheURL.GenerateCacheWithExistingData();
                    break;
                case 4:
                    DownloadCache.DownloadAllSongs();
                    break;
                case 5:
                    GenerateCacheURL.ConvertToProperCache();
                    break;
            }
        }
    }
}