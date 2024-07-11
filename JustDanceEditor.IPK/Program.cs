using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JustDanceEditor.IPK;
internal class Program
{
    private static void Main(string[] args)
    {
        List<string> list = [];
        // For each argument, remove if the file doesn't exist
        foreach (string arg in args)
        {
            if (File.Exists(arg))
            {
                list.Add(arg);
            }
            else
            {
                Console.WriteLine($"File {arg} doesn't exist.");
            }
        }

        // If there are no arguments, show the help
        if (list.Count == 0)
        {
            ShowHelp();
        }

        // For each file extract the IPK using the parser in parallel
        Parallel.ForEach(args, x =>
        {
            // Show the file being processed
            Console.WriteLine(x);

            // Create the output path
            string outputPath = Path.Combine(Path.GetDirectoryName(x)!, Path.GetFileNameWithoutExtension(x));

            // Create the parser
            JustDanceIPKParser parser = new(x, outputPath);
            parser.Parse();

            // Show that the file has been processed
            Console.WriteLine($"{x} has been processed.");
        });

        // Wait for any key
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
    }

    private static void ShowHelp()
    {
        // Show the help
        Console.WriteLine("Usage: JustDanceEditor.IPK.exe <file1> <file2> ...");
    }
}
