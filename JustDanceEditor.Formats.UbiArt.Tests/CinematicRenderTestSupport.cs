using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.Cinematics.Video;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Tests;

internal static class CinematicRenderTestSupport
{
    internal static MaterializedMashupSegment CreateMashupSegment(
        int blockIndex,
        int absoluteStartBeat,
        string sourceSongName = "Source",
        double outputStartSeconds = 0,
        double outputDurationSeconds = 4) =>
        new(
            "source.webm",
            BlockIndex: blockIndex,
            AbsoluteStartBeat: absoluteStartBeat,
            DurationBeats: 8,
            SourceFirstBeat: 0,
            SourceLastBeat: 8,
            SourceSongName: sourceSongName,
            SourceWidth: 1920,
            VisibleHeight: 1080,
            AlphaHeight: 0,
            HasStackedAlpha: false,
            SourceStartSeconds: 0,
            SourceDurationSeconds: 4,
            OutputStartSeconds: outputStartSeconds,
            OutputDurationSeconds: outputDurationSeconds,
            OffsetX: 0,
            OffsetY: 0,
            Scale: 1);


    internal static MaterializedCinematicImage CreateSolidImage(string key, string texturePath, Bgra32 color)
    {
        Image<Bgra32> image = new(8, 8, color);
        return new MaterializedCinematicImage(key, texturePath, image);
    }


    internal static MaterializedCinematicImage CreateSolidImage(
        string key,
        string texturePath,
        Bgra32 color,
        CinematicRenderMaterial material)
    {
        Image<Bgra32> image = new(8, 8, color);
        return new MaterializedCinematicImage(key, texturePath, image, RenderGeometry.NoAtlasQuad, material);
    }


    internal static void CreateStackedPleoSourceFrame(string path, Bgra32 color)
    {
        using Image<Bgra32> image = new(8, 12);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = y < 8 ? color : CreateColor(255, 255, 255);
            }
        });
        image.Save(path);
    }


    internal static PleoFrameSource CreateHalfMaskedPleoFrameSource()
    {
        Bgra32[] pixels = new Bgra32[8 * 12];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
                pixels[(y * 8) + x] = CreateColor(255, 0, 0);
        }

        for (int y = 8; y < 12; y++)
        {
            for (int x = 0; x < 8; x++)
                pixels[(y * 8) + x] = x < 4 ? CreateColor(255, 255, 255) : CreateColor(0, 0, 0);
        }

        Bgra32[] cutoutPixels = CinematicCpuDraw.CreatePleoCutoutPixels(
            pixels,
            stackWidth: 8,
            sourceWidth: 8,
            visibleHeight: 8,
            alphaHeight: 4);
        PleoUvBounds alphaBounds = CinematicCpuDraw.ComputePleoAlphaBounds(
            pixels,
            stackWidth: 8,
            sourceWidth: 8,
            visibleHeight: 8,
            alphaHeight: 4);
        return new PleoFrameSource(
            pixels,
            cutoutPixels,
            stackWidth: 8,
            sourceWidth: 8,
            visibleHeight: 8,
            alphaHeight: 4,
            alphaBounds,
            isCutout: false);
    }


    internal static PleoFrameSource CreateSoftAlphaPleoFrameSource()
    {
        Bgra32[] pixels = new Bgra32[8 * 12];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
                pixels[(y * 8) + x] = CreateColor(255, 0, 0);
        }

        for (int y = 8; y < 12; y++)
        {
            for (int x = 0; x < 8; x++)
                pixels[(y * 8) + x] = x < 4 ? CreateColor(126, 126, 126) : CreateColor(0, 0, 0);
        }

        Bgra32[] cutoutPixels = CinematicCpuDraw.CreatePleoCutoutPixels(
            pixels,
            stackWidth: 8,
            sourceWidth: 8,
            visibleHeight: 8,
            alphaHeight: 4);
        PleoUvBounds alphaBounds = CinematicCpuDraw.ComputePleoAlphaBounds(
            pixels,
            stackWidth: 8,
            sourceWidth: 8,
            visibleHeight: 8,
            alphaHeight: 4);
        return new PleoFrameSource(
            pixels,
            cutoutPixels,
            stackWidth: 8,
            sourceWidth: 8,
            visibleHeight: 8,
            alphaHeight: 4,
            alphaBounds,
            isCutout: false);
    }


    internal static CinematicRenderMaterial CreatePleoFullScreenCopyMaterial() =>
        new(
            CinematicFrameRenderer.GfxBlendCopy,
            [
                CinematicMaterialLayer.Default with
                {
                    AddressModeU = TextureAddressMode.Border,
                    AddressModeV = TextureAddressMode.Border
                }
            ]);


    internal static CinematicRenderMaterial CreateStackedPleoAlphaMaterial() =>
        new(
            CinematicFrameRenderer.GfxBlendAlpha,
            [
                CinematicMaterialLayer.Default with
                {
                    AddressModeU = TextureAddressMode.Border,
                    AddressModeV = TextureAddressMode.Border,
                    TextureUsage = CinematicTextureUsage.EntireTexture,
                    UvModifiers =
                    [
                        new CinematicUvModifier(
                            TranslationU: 0.0f,
                            TranslationV: 0.0f,
                            AnimTranslationU: false,
                            AnimTranslationV: false,
                            Rotation: 0.0f,
                            RotationOffsetU: 0.5f,
                            RotationOffsetV: 0.5f,
                            AnimRotation: false,
                            ScaleU: 1.0f,
                            ScaleV: 2.0f / 3.0f,
                            ScaleOffsetU: 0.0f,
                            ScaleOffsetV: 0.0f)
                    ]
                },
                CinematicMaterialLayer.Default with
                {
                    AddressModeU = TextureAddressMode.Border,
                    AddressModeV = TextureAddressMode.Border,
                    BlendMode = CinematicFrameRenderer.GfxBlendMul,
                    TextureUsage = CinematicTextureUsage.AlphaIsGreen,
                    UvModifiers =
                    [
                        new CinematicUvModifier(
                            TranslationU: 0.0f,
                            TranslationV: 2.0f / 3.0f,
                            AnimTranslationU: false,
                            AnimTranslationV: false,
                            Rotation: 0.0f,
                            RotationOffsetU: 0.5f,
                            RotationOffsetV: 0.5f,
                            AnimRotation: false,
                            ScaleU: 1.0f,
                            ScaleV: 1.0f / 3.0f,
                            ScaleOffsetU: 0.0f,
                            ScaleOffsetV: 0.0f)
                    ]
                }
            ]);


    internal static CinematicRenderMaterial CreatePleoAlphaMaskInsertMaterial() =>
        new(
            CinematicFrameRenderer.GfxBlendAlpha,
            [
                CinematicMaterialLayer.Default with
                {
                    AddressModeU = TextureAddressMode.Border,
                    AddressModeV = TextureAddressMode.Border,
                    TextureUsage = CinematicTextureUsage.NoTexture,
                    UvModifiers =
                    [
                        new CinematicUvModifier(
                            TranslationU: 0.0f,
                            TranslationV: 0.0f,
                            AnimTranslationU: false,
                            AnimTranslationV: false,
                            Rotation: 0.0f,
                            RotationOffsetU: 0.5f,
                            RotationOffsetV: 0.5f,
                            AnimRotation: false,
                            ScaleU: 1.0f,
                            ScaleV: 2.0f / 3.0f,
                            ScaleOffsetU: 0.5f,
                            ScaleOffsetV: 0.0f)
                    ]
                },
                CinematicMaterialLayer.Default with
                {
                    AddressModeU = TextureAddressMode.Border,
                    AddressModeV = TextureAddressMode.Border,
                    BlendMode = CinematicFrameRenderer.GfxBlendMul,
                    TextureUsage = CinematicTextureUsage.AlphaIsGreen,
                    UvModifiers =
                    [
                        new CinematicUvModifier(
                            TranslationU: 0.0f,
                            TranslationV: 2.0f / 3.0f,
                            AnimTranslationU: false,
                            AnimTranslationV: false,
                            Rotation: 0.0f,
                            RotationOffsetU: 0.5f,
                            RotationOffsetV: 0.5f,
                            AnimRotation: false,
                            ScaleU: 0.25f,
                            ScaleV: 1.0f / 3.0f,
                            ScaleOffsetU: 0.0f,
                            ScaleOffsetV: 0.0f)
                    ]
                },
                CinematicMaterialLayer.Default with
                {
                    BlendMode = CinematicFrameRenderer.GfxBlendMul,
                    TextureUsage = CinematicTextureUsage.EntireTexture
                }
            ]);


    internal static ProjectedQuad CreateScreenQuad(int width, int height) =>
        new(
            new Vector2(0, 0),
            new Vector2(width, 0),
            new Vector2(width, height),
            new Vector2(0, height),
            new Rectangle(0, 0, width, height));


    internal static RenderGeometry CreateTestMeshGeometry() =>
        RenderGeometry.FromMesh(
            [
                new RenderVertex(-1, -1, 0, 0, 1),
                new RenderVertex(1, -1, 0, 1, 1),
                new RenderVertex(1, 1, 0, 1, 0),
                new RenderVertex(-1, 1, 0, 0, 0)
            ],
            [0, 1, 2, 0, 2, 3],
            CinematicGeometrySource.Mesh3D);


    internal static ResolvedActorState CreateResolvedState() =>
        new(
            PositionX: 0,
            PositionY: 0,
            PositionZ: 0,
            ScaleX: 1,
            ScaleY: 1,
            Angle: 0,
            Alpha: 1,
            Tint: RgbTint.White,
            XFlipped: false);


    internal static Image<Bgra32> CreateSplitImage()
    {
        Image<Bgra32> image = new(8, 8);
        Bgra32 red = CreateColor(255, 0, 0);
        Bgra32 blue = CreateColor(0, 0, 255);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = x < row.Length / 2 ? red : blue;
            }
        });
        return image;
    }


    internal static Image<Bgra32> CreateSplitWhiteBlackImage()
    {
        Image<Bgra32> image = new(8, 8);
        Bgra32 white = CreateColor(255, 255, 255);
        Bgra32 black = CreateColor(0, 0, 0);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = x < row.Length / 2 ? white : black;
            }
        });
        return image;
    }


    internal static Bgra32 CreateColor(byte red, byte green, byte blue)
        => CreateColor(red, green, blue, alpha: 255);


    internal static Bgra32 CreateColor(byte red, byte green, byte blue, byte alpha)
    {
        Bgra32 color = default;
        color.R = red;
        color.G = green;
        color.B = blue;
        color.A = alpha;
        return color;
    }


}