using System.Diagnostics;

namespace JustDanceEditor.Converter.Resources;

public static class XTXExtractAdapter
{
    public static void ConvertToDDS(string inputPath, string outputPath)
    {
        // Run xtx_extract on the new file, parameters: -o {filename}.dds {filename}.xtx
        ProcessStartInfo startInfo = new()
        {
            FileName = "./Resources/xtx_extract.exe",
            Arguments = $"-o \"{Path.Combine(outputPath)}\" \"{Path.Combine(inputPath)}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Run the process and wait for it to exit
        using Process process = new() { StartInfo = startInfo };
        process.Start();

        process.WaitForExit();
    }
}
