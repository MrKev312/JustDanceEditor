using System.Reflection;

using JustDanceEditor.Helpers;
using JustDanceEditor.JD2Next;

namespace JustDanceEditor;

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
                default:
                    Console.WriteLine("The option is not valid.");
                    break;
            }
        }
    }

    private static void CacheStuff()
    {
        int choice = Question.Ask([
            "Exit",
            "Print the cache",
            "Generate cache for a song",
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
                   default:
                       Console.WriteLine("The option is not valid.");
                       break;
               }
    }

    private static void GenerateCache()
    {
        // Ask if the user wants to generate a song with existing data or urls
        int choice = Question.Ask([
            "Exit",
            "Print info about the cache",
            "Generate a song with URLs (manual data)",
            "Generate a song with URLs + songdb",
            "Generate a song with existing data (jd-s3.cdn.ubi.com + json)"
        ]);

        switch (choice)
        {
            case 0:
                return;
            case 1:
                Print.PrintCache();
                break;
            case 2:
                GenerateCacheURL.GenerateCacheWithUrls();
                break;
            case 3:
                GenerateCacheURL.GenerateCacheWithSongDBAndUrls();
                break;
            case 4:
                GenerateCacheURL.GenerateCacheWithExistingData();
                break;
            default:
                Console.WriteLine("The option is not valid.");
                break;
        }
    }
}