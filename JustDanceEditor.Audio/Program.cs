using NAudio.Wave;

namespace JustDanceEditor.Audio;

class Program
{
    static void Main(string[] args)
    {
        List<string> filePaths = [];

        if (args.Length == 0)
        {
#if DEBUG
            filePaths.Add(Console.ReadLine()!.Trim('"'));
#else
            Console.WriteLine("No files provided. Please provide file paths as arguments.");
            return;
#endif
        }
        else
        {
            foreach (string arg in args)
            {
                if (File.Exists(arg))
                    filePaths.Add(arg);
                else
                    Console.WriteLine($"File {arg} doesn't exist.");
            }
        }

        if (filePaths.Count == 0)
        {
            Console.WriteLine("Usage: JustDanceEditor.Audio.exe <file1> <file2> ...");
            return;
        }

        // Process each file in parallel
        Parallel.ForEach(filePaths, file =>
        {
            Console.WriteLine(file);

            try
            {
                // Skip if already opus
                if (file.EndsWith(".opus", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  Already Opus, skipping.");
                    return;
                }

                // Create output path (same directory, .opus extension)
                string outputPath = Path.Combine(
                    Path.GetDirectoryName(file)!,
                    Path.GetFileNameWithoutExtension(file) + ".opus");

                // Skip if output already exists
                if (File.Exists(outputPath))
                {
                    Console.WriteLine($"  Output file already exists, skipping.");
                    return;
                }

                // Convert to Opus
                using (FileStream inputStream = new(file, FileMode.Open, FileAccess.Read))
                using (WaveStream waveStream = new RakiAudioConverter().ConvertAsync(inputStream, Path.GetFileName(file)).GetAwaiter().GetResult())
                using (FileStream outputStream = new(outputPath, FileMode.Create, FileAccess.Write))
                {
                    MemoryStream opusStream = OpusEncoderHelper.EncodeToOpusStream(waveStream.ToSampleProvider());
                    opusStream.CopyTo(outputStream);
                    opusStream.Dispose();
                }

                Console.WriteLine($"  Converted to {Path.GetFileName(outputPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
        });

        // Wait for any key
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
    }
}