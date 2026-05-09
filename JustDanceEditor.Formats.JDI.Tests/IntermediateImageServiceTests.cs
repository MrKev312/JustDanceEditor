using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class IntermediateImageServiceTests
{
    [Fact]
    public async Task GenerateAllMissingImagesAsync_WhenNoBackgroundExists_CreatesPurpleBackgroundAndDerivedImages()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        try
        {
            IntermediateSongPackage package = new()
            {
                Metadata = new IntermediateMetadata
                {
                    MapName = "missingbg",
                    Title = "Missing BG",
                    Artist = "Tests",
                    CoachCount = 0,
                    OriginalJDVersion = 2019
                }
            };

            IntermediateImageService service = new(root, package, new SystemFileSystem());

            await service.GenerateAllMissingImagesAsync(TestContext.Current.CancellationToken);

            Assert.True(service.HasImage(ImageAssetType.MapBackground));
            Assert.True(service.HasImage(ImageAssetType.Banner));
            Assert.True(service.HasImage(ImageAssetType.AlbumBackground));
            Assert.True(service.HasImage(ImageAssetType.Cover));
            Assert.True(service.HasImage(ImageAssetType.SquareCover));

            string mapBackgroundPath = IntermediatePackageLayout.Resolve(root, IntermediatePackageLayout.Assets.MapBackgroundFile);
            using Image<Bgra32> mapBackground = await Image.LoadAsync<Bgra32>(mapBackgroundPath, TestContext.Current.CancellationToken);
            Bgra32 corner = mapBackground[0, 0];
            Assert.Equal(128, corner.R);
            Assert.Equal(0, corner.G);
            Assert.Equal(128, corner.B);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
