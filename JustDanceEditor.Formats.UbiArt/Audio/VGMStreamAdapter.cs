using Microsoft.Extensions.Logging;

using System.Diagnostics;
using System.IO.Compression;

namespace JustDanceEditor.Formats.UbiArt.Audio;

public sealed class VGMStreamAdapter : JDI.Services.IAudioConverter
{
    private static readonly JDI.Services.IFileSystem _io = new JDI.Services.SystemFileSystem();

    public static bool Exists() =>
        _io.DirectoryExists("Resources/VGMStream/") && _io.FileExists("Resources/VGMStream/vgmstream-cli.exe");

    public static async Task Download(ILogger? logger = null)
    {
        if (Exists())
            return;

        logger?.LogInformation("Downloading VGMStream...");

        _io.CreateDirectory("Resources/VGMStream/");

        string win64 = "https://github.com/vgmstream/vgmstream/releases/latest/download/vgmstream-win64.zip";
        string win32 = "https://github.com/vgmstream/vgmstream/releases/latest/download/vgmstream-win.zip";
        string downloadURL = Environment.Is64BitOperatingSystem ? win64 : win32;

        using HttpClient client = new();
        using HttpResponseMessage response = await client.GetAsync(downloadURL);
        if (response.IsSuccessStatusCode)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync();
            using ZipArchive archive = new(stream);
            archive.ExtractToDirectory("Resources/VGMStream/");
        }
        else
        {
            throw new Exception("Failed to download VGMStream, please check your internet connection.");
        }

        if (!Exists())
            throw new Exception("Failed to download VGMStream, please check your internet connection.");
    }

    public static async Task Check()
    {
        if (!Exists())
            await Download();
    }

    public async Task Convert(Stream input, string sourceFileName, string output, string? tempFolder = null)
    {
        await Check();

        string vgmFullPath = _io.GetFullPath("Resources/VGMStream/vgmstream-cli.exe");

        // If provided, place temp inputs under tempFolder/raw; otherwise fall back to system temp
        string workDir = tempFolder != null
            ? Path.Combine(tempFolder, "raw")
            : Path.GetTempPath();

        if (tempFolder != null)
            Directory.CreateDirectory(workDir);

        // Use provided sourceFileName directly (caller decides if .ckd should be appended)
        string tempInput = Path.Combine(workDir, sourceFileName);

        try
        {
            // Write the input stream to the temporary file and ensure it's flushed and closed before starting the process
            await using (FileStream fs = new(tempInput, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(fs);
                await fs.FlushAsync();
            }

            // Wait for the file to actually be created - retry a few times to handle Windows file system caching
            int retries = 5;
            while (!File.Exists(tempInput) && retries > 0)
            {
                await Task.Delay(50); // Wait 50ms before retrying
                retries--;
            }

            if (!File.Exists(tempInput))
                throw new Exception($"Temporary input file was not created: {tempInput}");

            Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = vgmFullPath,
                    Arguments = $"-o \"{output}\" \"{tempInput}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            process.Start();
            string stderr = await process.StandardError.ReadToEndAsync();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception($"vgmstream failed: {stderr}\n{stdout}");
        }
        finally
        {
            try
            {
                File.Delete(tempInput);
            }
            catch { }
        }
    }
}