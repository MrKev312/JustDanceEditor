using System.Reflection;

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
        
        string message = $"Version: {Assembly.GetExecutingAssembly().GetName().Version}-preview1";
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
                "Extract IPK file"
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
            }
        }
    }
}