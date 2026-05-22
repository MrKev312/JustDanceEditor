using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Diagnostics;

using Xunit;

namespace JustDanceEditor.Cli.Tests;

public sealed class CliContractTests
{
    [Fact]
    public async Task H_ShowsHelp_AndNIsHeadless()
    {
        CliResult result = await RunCliAsync("-h");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("-h, --help", result.Output);
        Assert.Contains("-n, --headless, --non-interactive", result.Output);
        Assert.DoesNotContain("Missing command", result.Output);
    }

    [Fact]
    public async Task AudioHelp_OnlyShowsAudioOptions()
    {
        CliResult result = await RunCliAsync("audio", "-h");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--input", result.Output);
        Assert.Contains("--output", result.Output);
        Assert.Contains("--encoding", result.Output);
        Assert.DoesNotContain("-t, --target", result.Output);
        Assert.DoesNotContain("--source", result.Output);
        Assert.DoesNotContain("--song", result.Output);
        Assert.DoesNotContain("--provider", result.Output);
        Assert.DoesNotContain("--platform", result.Output);
        Assert.DoesNotContain("--download-online-assets", result.Output);
    }

    [Fact]
    public async Task TargetsHelp_OnlyShowsTargetListingOptions()
    {
        CliResult result = await RunCliAsync("targets", "-h");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--platform", result.Output);
        Assert.DoesNotContain("--input", result.Output);
        Assert.DoesNotContain("--output", result.Output);
        Assert.DoesNotContain("--encoding", result.Output);
        Assert.DoesNotContain("--provider", result.Output);
    }

    [Fact]
    public async Task HeadlessAudioMissingInput_FailsCleanly()
    {
        CliResult result = await RunCliAsync("audio", "-n");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Missing required option '--input'.", result.Output);
        Assert.DoesNotContain("Unhandled exception", result.Output);
        Assert.DoesNotContain("StackTrace", result.Output);
    }

    [Fact]
    public async Task AudioCommand_RejectsJdiTargetOption()
    {
        CliResult result = await RunCliAsync("audio", "-n", "--target", "nx");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unrecognized command or argument '--target'.", result.Output);
    }

    [Fact]
    public async Task HeadlessAudioMissingEncoding_FailsCleanly()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"jde-cli-contract-{Guid.NewGuid():N}.wav");

        CliResult result = await RunCliAsync("audio", "-n", "--input", inputPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Missing required option '--encoding'.", result.Output);
        Assert.DoesNotContain("Unhandled exception", result.Output);
    }

    [Fact]
    public async Task HeadlessTextureMissingEncoding_FailsCleanly()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"jde-cli-contract-{Guid.NewGuid():N}.png");

        CliResult result = await RunCliAsync("texture", "-n", "--input", inputPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Missing required option '--encoding'.", result.Output);
        Assert.DoesNotContain("Unhandled exception", result.Output);
    }

    [Fact]
    public async Task DroppedAudioPath_UsesHeadlessEncoding()
    {
        using TempFolder temp = TempFolder.Create();
        string inputPath = Path.Combine(temp.Path, "sample.wav");
        string outputFolder = Path.Combine(temp.Path, "out");
        Directory.CreateDirectory(outputFolder);
        WriteSilentWave(inputPath);

        CliResult result = await RunCliAsync(inputPath, "-n", "--encoding", "wav", "--output", outputFolder, "--no-wait", "--force");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(outputFolder, "sample.wav")));
    }

    [Fact]
    public async Task DroppedTexturePath_UsesHeadlessEncoding()
    {
        using TempFolder temp = TempFolder.Create();
        string inputPath = Path.Combine(temp.Path, "sample.png");
        string outputFolder = Path.Combine(temp.Path, "out");
        Directory.CreateDirectory(outputFolder);
        using (Image<Rgba32> image = new(2, 2))
        {
            image[0, 0] = Color.Red;
            image[1, 0] = Color.Lime;
            image[0, 1] = Color.Blue;
            image[1, 1] = Color.White;
            image.SaveAsPng(inputPath);
        }

        CliResult result = await RunCliAsync(inputPath, "-n", "--encoding", "png", "--output", outputFolder, "--no-wait", "--force");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(outputFolder, "sample.png")));
    }

    [Fact]
    public async Task DroppedAudioPath_CanWriteNativeAndRakiConsoleTargets()
    {
        using TempFolder temp = TempFolder.Create();
        string inputPath = Path.Combine(temp.Path, "sample.wav");
        string nativeOutputFolder = Path.Combine(temp.Path, "native");
        string rakiOutputFolder = Path.Combine(temp.Path, "raki");
        Directory.CreateDirectory(nativeOutputFolder);
        Directory.CreateDirectory(rakiOutputFolder);
        WriteSilentWave(inputPath);

        CliResult nativeResult = await RunCliAsync(inputPath, "-n", "--encoding", "nx-opus", "--output", nativeOutputFolder, "--no-wait", "--force");
        CliResult rakiResult = await RunCliAsync(inputPath, "-n", "--encoding", "raki-nx-opus", "--output", rakiOutputFolder, "--no-wait", "--force");

        string nativeOutput = Path.Combine(nativeOutputFolder, "sample.lopus");
        string rakiOutput = Path.Combine(rakiOutputFolder, "sample.wav.ckd");
        Assert.Equal(0, nativeResult.ExitCode);
        Assert.Equal(0, rakiResult.ExitCode);
        Assert.True(File.Exists(nativeOutput));
        Assert.True(File.Exists(rakiOutput));
        Assert.NotEqual("RAKI", ReadAscii(nativeOutput, 0, 4));
        Assert.Equal("RAKI", ReadAscii(rakiOutput, 0, 4));
    }

    [Fact]
    public async Task DroppedTexturePath_CanWriteNativeAndCkdConsoleTargets()
    {
        using TempFolder temp = TempFolder.Create();
        string inputPath = Path.Combine(temp.Path, "sample.png");
        string nativeOutputFolder = Path.Combine(temp.Path, "native");
        string ckdOutputFolder = Path.Combine(temp.Path, "ckd");
        Directory.CreateDirectory(nativeOutputFolder);
        Directory.CreateDirectory(ckdOutputFolder);
        WriteTestImage(inputPath);

        CliResult nativeResult = await RunCliAsync(inputPath, "-n", "--encoding", "xtx-rgba8", "--output", nativeOutputFolder, "--no-wait", "--force");
        CliResult ckdResult = await RunCliAsync(inputPath, "-n", "--encoding", "ckd-xtx-rgba8", "--output", ckdOutputFolder, "--no-wait", "--force");

        string nativeOutput = Path.Combine(nativeOutputFolder, "sample.xtx");
        string ckdOutput = Path.Combine(ckdOutputFolder, "sample.xtx.ckd");
        Assert.Equal(0, nativeResult.ExitCode);
        Assert.Equal(0, ckdResult.ExitCode);
        Assert.True(File.Exists(nativeOutput));
        Assert.True(File.Exists(ckdOutput));
        Assert.NotEqual("TEX\0", ReadAscii(nativeOutput, 4, 4));
        Assert.Equal("TEX\0", ReadAscii(ckdOutput, 4, 4));
    }

    [Fact]
    public async Task DroppedMixedMediaPaths_UseSeparateHeadlessEncodings()
    {
        using TempFolder temp = TempFolder.Create();
        string audioOne = Path.Combine(temp.Path, "audio-one.wav");
        string audioTwo = Path.Combine(temp.Path, "audio-two.wav");
        string imageOne = Path.Combine(temp.Path, "image-one.png");
        string imageTwo = Path.Combine(temp.Path, "image-two.png");
        string outputFolder = Path.Combine(temp.Path, "out");
        Directory.CreateDirectory(outputFolder);

        WriteSilentWave(audioOne);
        WriteSilentWave(audioTwo);
        WriteTestImage(imageOne);
        WriteTestImage(imageTwo);

        CliResult result = await RunCliAsync(
            audioOne,
            audioTwo,
            imageOne,
            imageTwo,
            "-n",
            "--audio-encoding",
            "wav",
            "--texture-encoding",
            "png",
            "--output",
            outputFolder,
            "--no-wait",
            "--force");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(outputFolder, "audio-one.wav")));
        Assert.True(File.Exists(Path.Combine(outputFolder, "audio-two.wav")));
        Assert.True(File.Exists(Path.Combine(outputFolder, "image-one.png")));
        Assert.True(File.Exists(Path.Combine(outputFolder, "image-two.png")));
    }

    [Fact]
    public async Task DroppedFolderAndIpk_DispatchToPackAndExtract()
    {
        using TempFolder temp = TempFolder.Create();
        string sourceFolder = Path.Combine(temp.Path, "source");
        string ipkPath = Path.Combine(temp.Path, "source.ipk");
        string extractFolder = Path.Combine(temp.Path, "extract");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "hello.txt"), "hi");

        CliResult packResult = await RunCliAsync(sourceFolder, "-n", "--output", ipkPath, "--no-wait", "--force");
        CliResult extractResult = await RunCliAsync(ipkPath, "-n", "--output", extractFolder, "--no-wait", "--force");

        Assert.Equal(0, packResult.ExitCode);
        Assert.True(File.Exists(ipkPath));
        Assert.Equal(0, extractResult.ExitCode);
        Assert.True(Directory.Exists(extractFolder));
        Assert.True(Directory.EnumerateFileSystemEntries(extractFolder, "*", SearchOption.AllDirectories).Any());
    }

    private static async Task<CliResult> RunCliAsync(params string[] args)
    {
        string repoRoot = FindRepositoryRoot();
        string cliDll = FindCliDll(repoRoot);
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(cliDll);
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Failed to start CLI process.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException($"CLI process timed out: {string.Join(" ", args)}");
        }

        return new CliResult(process.ExitCode, $"{await standardOutput}{await standardError}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "JustDanceEditor.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static string FindCliDll(string repoRoot)
    {
        string[] configurations =
        [
#if DEBUG
            "Debug",
            "Release",
#else
            "Release",
            "Debug",
#endif
        ];

        foreach (string configuration in configurations)
        {
            string candidate = Path.Combine(repoRoot, "JustDanceEditor.Cli", "bin", configuration, "net10.0", "JustDanceEditor.Cli.dll");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not find built JustDanceEditor.Cli.dll.");
    }

    private static void WriteSilentWave(string path)
    {
        const int sampleRate = 8_000;
        const int samples = 800;
        const short channels = 1;
        const short bitsPerSample = 16;
        int dataBytes = samples * channels * bitsPerSample / 8;

        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        for (int i = 0; i < samples; i++)
            writer.Write((short)0);
    }

    private static void WriteTestImage(string path)
    {
        using Image<Rgba32> image = new(2, 2);
        image[0, 0] = Color.Red;
        image[1, 0] = Color.Lime;
        image[0, 1] = Color.Blue;
        image[1, 1] = Color.White;
        image.SaveAsPng(path);
    }

    private static string ReadAscii(string path, int offset, int count)
    {
        byte[] data = File.ReadAllBytes(path);
        return System.Text.Encoding.ASCII.GetString(data, offset, Math.Min(count, data.Length - offset));
    }

    private sealed record CliResult(int ExitCode, string Output);

    private sealed class TempFolder : IDisposable
    {
        private TempFolder(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempFolder Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jde-cli-contract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempFolder(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}