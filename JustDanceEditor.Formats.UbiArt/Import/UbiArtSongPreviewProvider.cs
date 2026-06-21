using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Preview;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.UbiArt.Import;

public sealed class UbiArtSongPreviewProvider(
    ISongDataLoader songDataLoader,
    Func<UbiArtConversionRequest, UbiArtVersionProfile, JustDanceUbiArtFileSystem> fileSystemFactory,
    IUbiArtEngineDetector engineDetector,
    ITextureService textureService,
    ILogger<UbiArtSongPreviewProvider> logger) : ISongPreviewProvider
{
    private readonly ISongDataLoader _songDataLoader = songDataLoader;
    private readonly Func<UbiArtConversionRequest, UbiArtVersionProfile, JustDanceUbiArtFileSystem> _fileSystemFactory = fileSystemFactory;
    private readonly IUbiArtEngineDetector _engineDetector = engineDetector;
    private readonly ITextureService _textureService = textureService;
    private readonly ILogger<UbiArtSongPreviewProvider> _logger = logger;

    public string FormatName => "UbiArt";
    public int Priority => 20;

    public bool CanPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!Directory.Exists(path) && !Path.GetExtension(path).Equals(".ipk", StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            UbiArtVersionProfile profile = _engineDetector.Detect(path);
            UbiArtConversionRequest request = new(path, Path.GetTempPath(), null)
            {
                Type = profile.Platform == UbiArtPlatform.Uncooked ? CookedType.Uncooked : CookedType.Cooked
            };

            JustDanceUbiArtFileSystem fileSystem = _fileSystemFactory(request, profile);
            fileSystem.Initialize();
            return fileSystem.GetAvailableSongs().Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public Task<SongPreviewResult> LoadPreviewAsync(SongPreviewRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        UbiArtVersionProfile profile = _engineDetector.Detect(request.InputPath);
        UbiArtConversionRequest conversionRequest = new(request.InputPath, request.WorkingRoot, request.SongName)
        {
            Type = profile.Platform == UbiArtPlatform.Uncooked ? CookedType.Uncooked : CookedType.Cooked
        };

        JustDanceUbiArtFileSystem fileSystem = _fileSystemFactory(conversionRequest, profile);
        fileSystem.Initialize();

        if (string.IsNullOrWhiteSpace(conversionRequest.SongName))
        {
            (string SongName, string SongDescPath)[] songs = fileSystem.GetAvailableSongs();
            if (songs.Length == 0)
                throw new InvalidOperationException("No songs found in the input bundle.");
            if (songs.Length > 1)
                throw new MultipleSongsFoundException(songs.Select(song => song.SongName));

            fileSystem.UpdateSongName(songs[0].SongName);
        }
        else
        {
            fileSystem.UpdateSongName(conversionRequest.SongName);
        }

        SongDesc songDesc = _songDataLoader.LoadSongDesc(conversionRequest, fileSystem);
        InfoComponent info = songDesc.Components.FirstOrDefault()
            ?? throw new InvalidDataException("SongDesc did not contain an InfoComponent.");

        IntermediateSongPackage package = new()
        {
            Metadata = BuildMetadata(info, profile, _logger)
        };

        string packageRoot = Path.Combine(request.WorkingRoot, package.Metadata.MapName);
        Directory.CreateDirectory(packageRoot);
        IntermediatePackageSerializer.WriteToFolder(package, packageRoot);
        TryWriteCoverAssets(fileSystem, packageRoot, info.MapName, cancellationToken);
        TryWriteBackgroundAssets(fileSystem, packageRoot, info.MapName, cancellationToken);
        TryWriteAlbumCoachAsset(fileSystem, packageRoot, info.MapName, cancellationToken);

        return Task.FromResult(new SongPreviewResult(
            package,
            FormatName,
            packageRoot,
            MaterializedRootIsTemporary: true));
    }

    private static IntermediateMetadata BuildMetadata(InfoComponent info, UbiArtVersionProfile profile, ILogger logger)
    {
        IntermediateMetadata metadata = new()
        {
            SongID = Guid.NewGuid(),
            MapName = info.MapName,
            ParentMapName = info.MapName,
            Title = info.Title,
            Artist = info.Artist,
            Credits = info.Credits,
            LyricsColor = ConvertColor(info.DefaultColors.Lyrics),
            OriginalJDVersion = info.OriginalJDVersion == 0 ? info.JDVersion : info.OriginalJDVersion,
            CoachCount = info.NumCoach,
            Difficulty = info.Difficulty,
            SweatDifficulty = NormalizeSweatDifficulty(info, logger),
            Tags = info.Tags?.ToList() ?? [],
            Status = info.Status,
            MojoValue = info.MojoValue,
            CountInProgression = info.CountInProgression
        };

        metadata.AdditionalMetadata["platformType"] = profile.Platform.ToString();
        metadata.AdditionalMetadata["videoPreviewPath"] = info.VideoPreviewPath;
        metadata.AdditionalMetadata["songcolor_1a"] = ConvertColor(info.DefaultColors.SongColor1a);
        metadata.AdditionalMetadata["songcolor_1b"] = ConvertColor(info.DefaultColors.SongColor1b);
        metadata.AdditionalMetadata["songcolor_2a"] = ConvertColor(info.DefaultColors.SongColor2a);
        metadata.AdditionalMetadata["songcolor_2b"] = ConvertColor(info.DefaultColors.SongColor2b);

        return metadata;
    }

    private static uint NormalizeSweatDifficulty(InfoComponent info, ILogger logger)
    {
        uint sweatDifficulty = info.EffectiveSweatDifficulty;
        if (sweatDifficulty != 0)
            return sweatDifficulty;

        logger.LogWarning(
            "UbiArt map '{MapName}' has SweatDifficulty/Energy 0; defaulting sweat difficulty to 1.",
            info.MapName);

        return 1;
    }

    private void TryWriteCoverAssets(JustDanceUbiArtFileSystem fileSystem, string packageRoot, string songName, CancellationToken cancellationToken)
    {
        string coverFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.CoverAssetsFolder);
        Directory.CreateDirectory(coverFolder);

        TryWriteSpecificCover(fileSystem, songName, packageRoot, cancellationToken);

        if (File.Exists(IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.SquareCoverFile)))
            return;

        CookedFile? fallback = fileSystem.AssetResolver?.GetCoverArt();
        if (fallback is null)
            return;

        TryWriteImage(fileSystem, fallback, IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.SquareCoverFile), square: true, cancellationToken);
    }

    private void TryWriteSpecificCover(JustDanceUbiArtFileSystem fileSystem, string songName, string packageRoot, CancellationToken cancellationToken)
    {
        CookedFile? squareCover = fileSystem.GetAllFiles(fileSystem.InputFolders.MenuArtFolder, $"{songName}_cover_generic.*").FirstOrDefault();
        squareCover ??= fileSystem.GetAllFiles(fileSystem.InputFolders.MenuArtFolder, $"{songName}_cover_online.*").FirstOrDefault();

        if (squareCover is not null)
            TryWriteImage(fileSystem, squareCover, IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.SquareCoverFile), square: true, cancellationToken);

        CookedFile? cover = fileSystem.AssetResolver?.GetCoverArt();
        if (cover is not null)
            TryWriteImage(fileSystem, cover, IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.CoverFile), square: false, cancellationToken);
    }

    private void TryWriteImage(JustDanceUbiArtFileSystem fileSystem, CookedFile file, string destination, bool square, CancellationToken cancellationToken)
    {
        TryWriteImage(fileSystem, file, destination, square ? PreviewImageKind.SquareCover : PreviewImageKind.WideCover, cancellationToken);
    }

    private void TryWriteBackgroundAssets(JustDanceUbiArtFileSystem fileSystem, string packageRoot, string songName, CancellationToken cancellationToken)
    {
        string backgroundsFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.BackgroundsFolder);
        Directory.CreateDirectory(backgroundsFolder);

        string mapBackgroundDestination = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.MapBackgroundFile);
        CookedFile? mapBackground = fileSystem.GetAllFiles(fileSystem.InputFolders.MenuArtFolder, $"{songName}_map_bkg.*").FirstOrDefault();
        if (mapBackground is not null)
        {
            TryWriteImage(fileSystem, mapBackground, mapBackgroundDestination, PreviewImageKind.Raw, cancellationToken);
            if (File.Exists(mapBackgroundDestination))
                return;
        }

        CookedFile? banner = fileSystem.GetAllFiles(fileSystem.InputFolders.MenuArtFolder, $"{songName}_banner_bkg.*").FirstOrDefault();
        if (banner is not null)
            TryWriteImage(fileSystem, banner, IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.BannerFile), PreviewImageKind.Raw, cancellationToken);
    }

    private void TryWriteAlbumCoachAsset(JustDanceUbiArtFileSystem fileSystem, string packageRoot, string songName, CancellationToken cancellationToken)
    {
        CookedFile? albumCoach = fileSystem.AssetResolver?.GetAlbumCoach();
        if (albumCoach is null)
            return;

        TryWriteImage(fileSystem, albumCoach, IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.AlbumCoachFile), PreviewImageKind.Raw, cancellationToken);
    }

    private void TryWriteImage(JustDanceUbiArtFileSystem fileSystem, CookedFile file, string destination, PreviewImageKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using Stream stream = fileSystem.GetFileStream(file);
            using Image<Bgra32>? image = _textureService.ConvertToImage(stream);
            if (image is null)
                return;

            if (kind == PreviewImageKind.SquareCover)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(512, 512),
                    Mode = ResizeMode.Crop
                }));
            }
            else if (kind == PreviewImageKind.WideCover)
            {
                if (image.Width < image.Height * 1.25)
                    return;

                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(640, 360),
                    Mode = ResizeMode.Crop
                }));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException($"Could not resolve output folder for '{destination}'."));
            image.Save(destination, JDI.Utilities.WebpSettings.LosslessWebpEncoder);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load preview image from {ImagePath}", file.RelativePath);
        }
    }

    private enum PreviewImageKind
    {
        SquareCover,
        WideCover,
        Raw
    }

    private static string ConvertColor(float[] rgba)
    {
        if (rgba == null || rgba.Length < 4)
            return "#FFFFFFFF";

        int a = (int)(rgba[0] * 255);
        int r = (int)(rgba[1] * 255);
        int g = (int)(rgba[2] * 255);
        int b = (int)(rgba[3] * 255);
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }
}
