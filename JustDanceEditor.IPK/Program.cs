namespace JustDanceEditor.IPK;

internal class Program
{
    private static void Main(string[] args)
    {
        bool printOnly = args.Contains("--print");
        List<string> pathArgs = [.. args.Where(a => !a.StartsWith("--"))];

        // If there are no arguments, show the help
        if (pathArgs.Count == 0)
        {
            ShowHelp();
            return;
        }

        // Process sequentially to keep console output clean when mixing types
        foreach (string path in pathArgs)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // --- PACKING MODE ---
                    // Input is a folder, create IPK next to it
                    string parentDir = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar))
                                       ?? Path.GetPathRoot(path)!;
                    string folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                    string outputPath = Path.Combine(parentDir, folderName + ".ipk");

                    Console.WriteLine($"\n=== Packing: {path} ===");
                    JustDanceIPKWriter writer = new(path, outputPath);
                    writer.Pack();
                }
                else if (File.Exists(path))
                {
                    // --- EXTRACT/INFO MODE ---
                    Console.WriteLine($"\n=== Processing File: {path} ===");

                    if (printOnly)
                    {
                        JustDanceIPKParser parser = new(path, "");
                        parser.ParseInfo();
                    }
                    else
                    {
                        string outputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
                        JustDanceIPKParser parser = new(path, outputPath);
                        parser.Parse(ShowInfo: true); // Enabled ShowInfo for better feedback
                        Console.WriteLine($"Extracted to: {outputPath}");
                    }
                }
                else
                {
                    Console.WriteLine($"Path not found: {path}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {path}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        if (!args.Contains("--no-wait"))
        {
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage: JustDanceEditor.IPK.exe [options] <path1> <path2> ...");
        Console.WriteLine("  <path>   Can be an .ipk file (to extract) or a folder (to pack)");
        Console.WriteLine("Options:");
        Console.WriteLine("  --print  Print file information without extracting (files only)");
    }
}