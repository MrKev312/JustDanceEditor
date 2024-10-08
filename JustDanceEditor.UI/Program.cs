﻿using System.Reflection;

using JustDanceEditor.UI.Helpers;
using JustDanceEditor.UI.Converting;
using JustDanceEditor.Logging;

namespace JustDanceEditor.UI;

internal class Program
{
    static void Main()
    {
        // Delete old log
        Logger.ClearLog();

        Console.WriteLine("Just Dance Editor");
        Console.WriteLine("Made by: MrKev312");

        // Get version from nerdbank.gitversioning
        string message = $"Version: {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}";

        Logger.Log(message, LogLevel.Debug);
        Console.WriteLine(message);
        message = $"Current Directory: {Environment.CurrentDirectory}";
        Logger.Log(message, LogLevel.Debug);
        Console.WriteLine(message);

        MainLoop();
        Console.WriteLine("Exiting...");
    }

    static void MainLoop()
    {
        while (true)
        {
            int choice = Question.Ask([
                "Exit",
                "Convert UbiArt to Unity",
                "Convert UbiArt to Unity (Advanced)",
                "Convert all songs in folder",
                "Extract IPK file",
                "Generate a new cache",
                "Spread cache folders over 0029 (use for exFAT only)"
            ]);

            switch (choice)
            {
                case 0:
                    return;
                case 1:
                    ConverterDialogue.ConvertSingleDialogue();
                    break;
                case 2:
                    ConverterDialogue.ConvertSingleDialogueAdvanced();
                    break;
                case 3:
                    ConverterDialogue.ConvertAllSongsInFolder();
                    break;
                case 4:
                    ExtractorDialogue.ExtractDialogue();
                    break;
                case 5:
                    CacheDialogue.GenerateCacheDialogue();
                    break;
                case 6:
                    CacheDialogue.SpreadCacheDialogue();
                    break;
            }
        }
    }
}