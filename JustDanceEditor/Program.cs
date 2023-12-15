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
                "Print the cache",
                "Generate cache for a song",
                "See which folders differ from the cache",
                "Convert UbiArt to Unity"
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
                case 4:
                    ConvertUbiArtToUnity.Convert();
                    break;
                default:
                    Console.WriteLine("The option is not valid.");
                    break;
            }
        }
    }

    private static void GenerateCache()
    {
        // Ask if the user wants to generate a song with existing data or urls
        int choice = Question.Ask([
            "Exit",
            "Generate a song with existing data",
            "Generate a song with urls",
            "Generate a song using songDB + URLs"
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
            default:
                Console.WriteLine("The option is not valid.");
                break;
        }
    }
}