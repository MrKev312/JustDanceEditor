using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class IntermediateImageServiceTests
{
    [Fact]
    public async Task GetSquareCoverAsync_Generates_FromBackgroundAndAlbumCoach_NotWideCover()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        try
        {
            IntermediateSongPackage package = new()
            {
                Metadata = new IntermediateMetadata
                {
                    MapName = "squarecover",
                    Title = "Square Cover",
                    Artist = "Tests",
                    CoachCount = 1,
                    OriginalJDVersion = 2022
                }
            };

            IntermediateImageService service = new(root, package, new SystemFileSystem());

            using Image<Bgra32> background = new(2048, 1024);
            background.Mutate(context => context.BackgroundColor(Color.Blue));
            await service.StoreImageAsync(background, ImageAssetType.MapBackground, cancellationToken: TestContext.Current.CancellationToken);

            using Image<Bgra32> albumCoach = new(1024, 1024);
            albumCoach.Mutate(context => context.BackgroundColor(Color.Red));
            await service.StoreImageAsync(albumCoach, ImageAssetType.AlbumCoach, cancellationToken: TestContext.Current.CancellationToken);

            using Image<Bgra32> squareCover = await service.GetSquareCoverAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(512, squareCover.Width);
            Assert.Equal(512, squareCover.Height);

            Bgra32 topLeft = squareCover[0, 0];
            Assert.True(topLeft.R > 240);
            Assert.True(topLeft.G < 10);
            Assert.True(topLeft.B < 10);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
