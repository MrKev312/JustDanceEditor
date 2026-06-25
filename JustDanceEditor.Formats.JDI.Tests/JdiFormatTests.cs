using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class JdiFormatTests
{
    [Fact]
    public async Task ExportAsync_WhenImportedJdiHasNoSuggestedOutput_UsesRequestOutputPathAndLeavesSourceImagesUntouched()
    {
        string sourceRoot = CreateTempDirectory();
        string outputRoot = CreateTempPath();

        try
        {
            await CreatePackageOnDiskAsync(sourceRoot, "Passthrough", TestContext.Current.CancellationToken);

            string sourceCoverPath = IntermediatePackageLayout.Resolve(sourceRoot, IntermediatePackageLayout.Assets.CoverFile);
            string sourceSquareCoverPath = IntermediatePackageLayout.Resolve(sourceRoot, IntermediatePackageLayout.Assets.SquareCoverFile);
            string sourceBannerPath = IntermediatePackageLayout.Resolve(sourceRoot, IntermediatePackageLayout.Assets.BannerFile);
            string sourceAlbumBackgroundPath = IntermediatePackageLayout.Resolve(sourceRoot, IntermediatePackageLayout.Assets.AlbumBackgroundFile);

            JdiFormat format = new();
            JdiImportResult result = await format.ImportAsync(
                new JdiConversionRequest(sourceRoot, outputRoot),
                TestContext.Current.CancellationToken);

            Assert.Null(result.SuggestedOutputFolder);

            await format.ExportAsync(
                result,
                new JdiConversionRequest(sourceRoot, outputRoot),
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(Path.Combine(outputRoot, IntermediatePackageLayout.MetadataFile)));
            Assert.True(File.Exists(Path.Combine(outputRoot, "source-note.txt")));
            Assert.True(File.Exists(IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.MapBackgroundFile)));
            Assert.True(File.Exists(IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.CoverFile)));
            Assert.True(File.Exists(IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.SquareCoverFile)));

            Assert.False(File.Exists(sourceCoverPath));
            Assert.False(File.Exists(sourceSquareCoverPath));
            Assert.False(File.Exists(sourceBannerPath));
            Assert.False(File.Exists(sourceAlbumBackgroundPath));

            IntermediateSongPackage exported = IntermediatePackageSerializer.LoadFromFolder(outputRoot);
            Assert.Equal("Passthrough", exported.Metadata.MapName);
        }
        finally
        {
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenOutputPathIsSource_GeneratesMissingImagesInPlace()
    {
        string sourceRoot = CreateTempDirectory();

        try
        {
            await CreatePackageOnDiskAsync(sourceRoot, "SameFolder", TestContext.Current.CancellationToken);

            JdiFormat format = new();
            JdiImportResult result = await format.ImportAsync(
                new JdiConversionRequest(sourceRoot, sourceRoot),
                TestContext.Current.CancellationToken);

            await format.ExportAsync(
                result,
                new JdiConversionRequest(sourceRoot, sourceRoot + Path.DirectorySeparatorChar),
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(Path.Combine(sourceRoot, IntermediatePackageLayout.MetadataFile)));
            Assert.True(File.Exists(Path.Combine(sourceRoot, "source-note.txt")));
            Assert.True(File.Exists(IntermediatePackageLayout.Resolve(sourceRoot, IntermediatePackageLayout.Assets.CoverFile)));
            Assert.True(File.Exists(IntermediatePackageLayout.Resolve(sourceRoot, IntermediatePackageLayout.Assets.SquareCoverFile)));
        }
        finally
        {
            DeleteDirectoryIfExists(sourceRoot);
        }
    }

    private static async Task CreatePackageOnDiskAsync(string root, string mapName, CancellationToken cancellationToken)
    {
        IntermediatePackageSerializer.WriteToFolder(CreatePackage(mapName), root);
        File.WriteAllText(Path.Combine(root, "source-note.txt"), "copied");
        await WriteMapBackgroundAsync(root, cancellationToken);
    }

    private static IntermediateSongPackage CreatePackage(string mapName) =>
        new()
        {
            Metadata = new IntermediateMetadata
            {
                MapName = mapName,
                ParentMapName = mapName,
                Title = mapName,
                Artist = "Test Artist",
                CoachCount = 0,
                OriginalJDVersion = 2024,
                LyricsColor = "#FFFFFFFF"
            }
        };

    private static async Task WriteMapBackgroundAsync(string root, CancellationToken cancellationToken)
    {
        string path = IntermediatePackageLayout.Resolve(root, IntermediatePackageLayout.Assets.MapBackgroundFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using Image<Bgra32> image = new(2048, 1024);
        image.Mutate(context => context.BackgroundColor(Color.FromRgb(24, 96, 180)));

        await WebpImageFormatProvider.Lossless.SaveAsync(image, path, cancellationToken);
    }

    private static string CreateTempDirectory()
    {
        string path = CreateTempPath();
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateTempPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "JustDanceEditor.Formats.JDI.Tests");
        Directory.CreateDirectory(root);
        return Path.Combine(root, Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }
}