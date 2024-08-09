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
        Console.WriteLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version}-preview1");
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
                "Convert UbiArt to Unity",
                "Convert UbiArt to Unity (Advanced)",
                "Extract IPK file"
            ]);

            switch (choice)
            {
                case 0:
                    return;
                case 1:
                    ConverterDialogue.ConvertDialogue();
                    break;
                case 2:
                    ConverterDialogue.ConvertDialogueAdvanced();
                    break;
                case 3:
                    ExtractorDialogue.ExtractDialogue();
                    break;
            }
        }
    }
}