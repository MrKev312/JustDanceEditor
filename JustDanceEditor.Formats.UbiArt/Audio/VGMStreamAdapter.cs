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

    public async Task Convert(string input, string output)
    {
        await Check();

        string vgmFullPath = _io.GetFullPath("Resources/VGMStream/vgmstream-cli.exe");

        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = vgmFullPath,
                Arguments = $"-o \"{output}\" \"{input}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        process.Start();
        process.WaitForExit();
    }
}