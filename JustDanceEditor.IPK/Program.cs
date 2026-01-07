namespace JustDanceEditor.IPK;

internal class Program
{
    private static void Main(string[] args)
    {
        bool printOnly = args.Contains("--print");
        List<string> fileArgs = [.. args.Where(a => !a.StartsWith("--"))];

        List<string> list = [];
        // For each argument, remove if the file doesn't exist
        foreach (string arg in fileArgs)
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
            return;
        }

        // For each file extract the IPK using the parser in parallel (unless --print is used)
        if (printOnly)
        {
            // Print mode: sequential processing with ShowInfo enabled
            foreach (var file in list)
            {
                try
                {
                    Console.WriteLine($"\n=== {file} ===");
                    JustDanceIPKParser parser = new(file, "");
                    parser.ParseInfo();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {file}: {ex.Message}");
                }
            }
        }
        else
        {
            // Extract mode: parallel processing
            Parallel.ForEach(list, x =>
            {
                try
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
                }
                catch (Exception ex)
                {
                    // Show the exception
                    Console.WriteLine($"Error processing {x}: {ex.Message}");
                }
            });

            // Wait for any key
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }

    private static void ShowHelp()
    {
        // Show the help
        Console.WriteLine("Usage: JustDanceEditor.IPK.exe [--print] <file1> <file2> ...");
        Console.WriteLine("  --print  Print file information without extracting");
    }
}