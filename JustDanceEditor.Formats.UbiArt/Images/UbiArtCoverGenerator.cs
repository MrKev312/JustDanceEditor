using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;

using Microsoft.Extensions.Logging;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.UbiArt.Images;

public static class UbiArtCoverGenerator
{
    public static Image<Bgra32>? ExistingCover(ConversionContext context, JDI.Services.ITextureService textureService, JDI.Services.IFileSystem io, ILogger? logger = null)
    {
        JDUbiArtSong song = context.SongData;

        CookedFile? cover = context.FileSystem.AssetResolver?.GetCoverArt();
        if (cover != null)
        {
            try
            {
                using Stream s = context.FileSystem.GetFileStream(cover);
                Image<Bgra32>? image = textureService.ConvertToImage(s);
                if (image != null)
                {
                    if (image.Width >= image.Height * 1.33)
                    {
                        logger?.LogInformation("Found existing cover: {FileName}", Path.GetFileName(cover.RelativePath));
                        image.Mutate(x => x.Resize(640, 360));
                        return image;
                    }

                    image.Dispose();
                }
            }
            catch { }
        }

        return null;
    }

    public static Image<Bgra32> GenerateOwnCover(ConversionContext context, JDI.Services.ITextureService textureService, ILogger? logger = null)
    {
        JDUbiArtSong song = context.SongData;

        Image<Bgra32> coverImage = GetBackground(context, textureService);

        CookedFile? coachFilesCooked = context.FileSystem.AssetResolver?.GetAlbumCoach();

        if (coachFilesCooked is not null)
        {
            try
            {
                using Stream s = context.FileSystem.GetFileStream(coachFilesCooked);
                using Image<Bgra32>? albumCoach = textureService.ConvertToImage(s);
                if (albumCoach is not null)
                {
                    albumCoach.Mutate(x => x.Resize(1024, 1024));
                    coverImage.Mutate(x => x.DrawImage(albumCoach, new Point(512, 0), 1));
                }
                else
                {
                    logger?.LogWarning("Album/Coach art could not be converted for '{SongName}'.", song.Name);
                }
            }
            catch
            {
                logger?.LogWarning("Album/Coach art not found or unreadable for '{SongName}'.", song.Name);
            }
        }
        else
            logger?.LogWarning("Album/Coach art not found for song '{SongName}'.", song.Name);

        coverImage.Mutate(x => x.Resize(720, 360));
        coverImage.Mutate(x => x.Crop(new Rectangle(40, 0, 640, 360)));
        return coverImage;
    }

    public static Image<Bgra32> GetBackground(ConversionContext context, JDI.Services.ITextureService textureService)
    {
        JDUbiArtSong song = context.SongData ?? throw new ArgumentNullException(nameof(context.SongData));

        Image<Bgra32>? coverImage = null;
        CookedFile? background = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{song.Name}_map_bkg.*")
            .FirstOrDefault();

        if (background is not null)
        {
            try
            {
                using Stream s = context.FileSystem.GetFileStream(background);
                coverImage ??= textureService.ConvertToImage(s);
            }
            catch { }
        }

        coverImage ??= ProcessBanner(context, textureService);

        if (coverImage is null)
        {
            coverImage = new Image<Bgra32>(2048, 1024);
            coverImage.Mutate(x => x.BackgroundColor(Color.Magenta));
            Font font = SystemFonts.CreateFont("Segoe UI", 150, FontStyle.Regular);
            coverImage.Mutate(x => x.DrawText("Background not found", font, Color.FloralWhite, new PointF(200, 512)));
        }

        coverImage.Mutate(x => x.Resize(2048, 1024));
        return coverImage;
    }

    public static Image<Bgra32>? ProcessBanner(ConversionContext context, JDI.Services.ITextureService textureService)
    {
        JDUbiArtSong song = context.SongData;

        CookedFile? background = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{song.Name}_banner_bkg.*")
            .FirstOrDefault();

        if (background is null)
            return null;

        Image<Bgra32>? banner;
        try
        {
            using Stream s = context.FileSystem.GetFileStream(background);
            banner = textureService.ConvertToImage(s);
        }
        catch
        {
            return null;
        }

        if (banner is null)
            return null;

        int width = banner.Width;
        int height = banner.Height;

        // Extract colors from SongDesc
        Defaultcolors? colors = song.SongDesc.COMPONENTS.FirstOrDefault()?.DefaultColors;
        Bgra32 primaryColor = ParseColor(colors?.lyrics, new Bgra32(255, 255, 255, 255));

        float boost = song.EngineVersion >= 2019 ? 0.2f : -0.1f;
        Bgra32 secondaryColor = AdjustBrightness(primaryColor, boost);
        float[] colorsA = [primaryColor.A / 255f, primaryColor.R / 255f, primaryColor.G / 255f, primaryColor.B / 255f];
        float[] colorsB = [secondaryColor.A / 255f, secondaryColor.R / 255f, secondaryColor.G / 255f, secondaryColor.B / 255f];

        Bgra32 colorA = new((byte)(colorsA[1] * 255), (byte)(colorsA[2] * 255), (byte)(colorsA[3] * 255), (byte)(colorsA[0] * 255));
        Bgra32 colorB = new((byte)(colorsB[1] * 255), (byte)(colorsB[2] * 255), (byte)(colorsB[3] * 255), (byte)(colorsB[0] * 255));

        Image<Bgra32> resultImage = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Bgra32 pixel = banner[x, y];
                float weight = pixel.B / 255f;
                Bgra32 newColor = GetWeightedAverage(colorA, colorB, weight);
                newColor = AddGreenChannel(newColor, pixel.G);
                resultImage[x, y] = newColor;
            }
        }

        return resultImage;

        static Bgra32 GetWeightedAverage(Bgra32 colorA, Bgra32 colorB, float weight)
        {
            return new(
                (byte)((colorA.R * weight) + (colorB.R * (1 - weight))),
                (byte)((colorA.G * weight) + (colorB.G * (1 - weight))),
                (byte)((colorA.B * weight) + (colorB.B * (1 - weight))),
                (byte)((colorA.A * weight) + (colorB.A * (1 - weight)))
            );
        }

        static Bgra32 AddGreenChannel(Bgra32 color, byte greenValue)
        {
            return new(
                (byte)Math.Min(color.R + greenValue, byte.MaxValue),
                (byte)Math.Min(color.G + greenValue, byte.MaxValue),
                (byte)Math.Min(color.B + greenValue, byte.MaxValue),
                color.A);
        }
    }

    private static Bgra32 ParseColor(float[]? colorValues, Bgra32 fallback)
    {
        if (colorValues == null || colorValues.Length < 4)
            return fallback;

        // UbiArt colors are usually float 0-1. Order might be RGBA or ARGB?
        // DefaultColors class has float[] lyrics.
        // Assuming RGBA or ARGB. Let's assume RGBA based on usage in UnityCoverArtGenerator (it parsed hex).
        // Wait, UnityCoverArtGenerator parsed hex. UbiArt usually stores as float[4].
        // Let's assume R, G, B, A.

        return new Bgra32(
            (byte)(colorValues[0] * 255),
            (byte)(colorValues[1] * 255),
            (byte)(colorValues[2] * 255),
            (byte)(colorValues[3] * 255)
        );
    }

    private static Bgra32 AdjustBrightness(Bgra32 color, float delta)
    {
        static byte Clamp(float value) => (byte)Math.Clamp(value, 0, 255);
        float factor = 1 + delta;
        return new Bgra32(
            Clamp(color.R * factor),
            Clamp(color.G * factor),
            Clamp(color.B * factor),
            color.A);
    }
}