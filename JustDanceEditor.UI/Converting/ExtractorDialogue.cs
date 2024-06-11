using JustDanceEditor.IPK;
using JustDanceEditor.UI.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JustDanceEditor.UI.Converting;
internal class ExtractorDialogue
{
    public static void ExtractDialogue()
    {
        // Ask for the input and output path
        string inputPath = Question.AskFile("Enter the path to the map folder you want to convert (the one containing cache and world): ", true);
        string outputPath = Question.AskFolder("Enter the path to the output folder: ", false);

        // Create the output folder if it doesn't exist
        Directory.CreateDirectory(outputPath);

        // Extract the IPK file
        JustDanceIPKParser parser = new(inputPath, outputPath);
        parser.Parse();
    }
}
