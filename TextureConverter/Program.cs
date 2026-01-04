using SixLabors.ImageSharp;

namespace TextureConverter;

class Program
{
    static void Main(string[] args)
    {
        List<string> list = [];
        if (args.Length == 0)
        {
#if DEBUG
            list.Add(Console.ReadLine()!.Trim('"'));
#else
            Console.WriteLine("No files provided. Please provide file paths as arguments.");
            return;
#endif
        }

        // For each argument, remove if the file doesn't exist
        foreach (string arg in args)
        {
            if (File.Exists(arg))
                list.Add(arg);
            else
                Console.WriteLine($"File {arg} doesn't exist.");
        }

        // If there are no arguments, show the help
        if (list.Count == 0)
            Console.WriteLine("Usage: TextureConverter.exe <file1> <file2> ...");

        // Register custom image formats with ImageSharp
        Formats.ImageSharpConfiguration.RegisterCustomFormats();

        // For each file extract the images in parallel
        Parallel.ForEach(list, x =>
        {
            // Show the file being processed
            Console.WriteLine(x);

            // Create the output path
            string outputPath = Path.Combine(Path.GetDirectoryName(x)!, Path.ChangeExtension(x, "png"));

            try
            {
                // Load using ImageSharp so our custom decoders can be used
                using Image image = Image.Load(x);
                image.SaveAsPng(outputPath);

                // Show that the file has been processed
                Console.WriteLine($"{x} has been processed.");
            }
            catch (Exception e)
            {
                // Show the error
                Console.WriteLine($"Error processing {x}: {e.Message}");
                return;
            }
        });

        // Wait for any key
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
    }
}