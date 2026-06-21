using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.Unity.Bundles;
using JustDanceEditor.Formats.Unity.Bundles.Synthesis;
using JustDanceEditor.Formats.Unity.Models;

using KevInc.Texture;

using Microsoft.Extensions.Logging.Abstractions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.IO;
using System.Linq;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public sealed class UnitySyntheticBundleFactoryTests
{
    private const string ExpectedSyntheticUnityVersion = "2021.3.40f1";
    private const string ExpectedSerializedFileUnityVersion = "0.0.0";
    private const string BlankCabName = "CAB-00000000000000000000000000000000";

    [Fact]
    public void CreateEmptyBundle_CreatesReadableUnityFsBundle()
    {
        byte[] bundleBytes = UnitySyntheticBundleFactory.CreateEmptyBundle();

        using MemoryStream stream = new(bundleBytes);
        AssetBundleFile bundle = new();
        try
        {
            bundle.Read(new AssetsFileReader(stream));

            Assert.Equal("UnityFS", bundle.Header.Signature);
            Assert.Single(bundle.BlockAndDirInfo.DirectoryInfos);
            Assert.True(bundle.IsAssetsFile(0));
        }
        finally
        {
            bundle.Close();
        }
    }

    [Fact]
    public void CreateEmptyBundle_UsesCurrentServerUnityVersion()
    {
        byte[] bundleBytes = UnitySyntheticBundleFactory.CreateEmptyBundle();

        Assert.Equal(ExpectedSyntheticUnityVersion, ReadBundleEngineVersion(bundleBytes));
    }

    [Fact]
    public void CreateEmptyBundle_AssignsUniqueNonBlankCabName()
    {
        string firstCabName = ReadBundleCabName(UnitySyntheticBundleFactory.CreateEmptyBundle());
        string secondCabName = ReadBundleCabName(UnitySyntheticBundleFactory.CreateEmptyBundle());

        Assert.StartsWith("CAB-", firstCabName, StringComparison.Ordinal);
        Assert.StartsWith("CAB-", secondCabName, StringComparison.Ordinal);
        Assert.NotEqual(BlankCabName, firstCabName);
        Assert.NotEqual(BlankCabName, secondCabName);
        Assert.NotEqual(firstCabName, secondCabName);
    }

    [Fact]
    public void EmbeddedClassData_CanCreateSyntheticImageBundle()
    {
        using Image<Rgba32> image = new(4, 4, Color.HotPink);
        UnityImageBundleAsset asset = new(
            "synthetic_cover",
            "synthetic_cover_Texture",
            "synthetic_cover_Sprite",
            image,
            4,
            4,
            TextureFormat.DXT1Crunched);

        using Stream classData = UnityClassDataProvider.OpenClassPackageStream();
        byte[] bundleBytes = UnitySyntheticBundleFactory.CreateImageBundle(
            classData,
            "Synthetic_Cover",
            [asset]);

        Assert.NotEmpty(bundleBytes);
    }

    [Fact]
    public void CreateImageBundle_CreatesReadableBundleWithoutTemplateBundle()
    {
        string classDataPath = FindClassDataPackage();
        using Image<Rgba32> image = new(4, 4, Color.HotPink);
        UnityImageBundleAsset asset = new(
            "synthetic_cover",
            "synthetic_cover_Texture",
            "synthetic_cover_Sprite",
            image,
            4,
            4,
            TextureFormat.DXT1Crunched);

        byte[] bundleBytes = UnitySyntheticBundleFactory.CreateImageBundle(
            classDataPath,
            "Synthetic_Cover",
            [asset]);

        using MemoryStream stream = new(bundleBytes, writable: false);
        AssetsManager manager = new();
        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(stream, "synthetic.bundle", unpackIfPacked: true);
            AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, 0, loadDeps: false);
            Assert.Equal(ExpectedSerializedFileUnityVersion, assets.file.Metadata.UnityVersion);

            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.AssetBundle));
            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.Texture2D));
            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.Sprite));

            AssetTypeValueField assetBundle = manager.GetBaseField(assets, assets.file.GetAssetsOfType(AssetClassID.AssetBundle)[0]);
            Assert.Equal("Synthetic_Cover", assetBundle["m_Name"].AsString);
            Assert.Equal(1u, assetBundle["m_RuntimeCompatibility"].AsUInt);
            Assert.Equal(1, assetBundle["m_ExplicitDataLayout"].AsInt);
            Assert.Equal(2, assetBundle["m_PreloadTable"]["Array"].Children.Count);
            Assert.Equal(2, assetBundle["m_Container"]["Array"].Children.Count);

            AssetTypeValueField sprite = manager.GetBaseField(assets, assets.file.GetAssetsOfType(AssetClassID.Sprite)[0]);
            Assert.Equal(1u, sprite["m_Extrude"].AsUInt);
            Assert.Equal(21300000, sprite["m_RenderDataKey"]["second"].AsLong);
            Assert.Single(sprite["m_RD"]["m_SubMeshes"]["Array"].Children);
            Assert.Equal(12, sprite["m_RD"]["m_IndexBuffer"]["Array"].AsByteArray.Length);
            Assert.Equal([3, 0, 0, 0, 1, 0, 2, 0, 1, 0, 0, 0], sprite["m_RD"]["m_IndexBuffer"]["Array"].AsByteArray);
            Assert.Equal(4u, sprite["m_RD"]["m_VertexData"]["m_VertexCount"].AsUInt);
            Assert.Equal(14, sprite["m_RD"]["m_VertexData"]["m_Channels"]["Array"].Children.Count);
            Assert.Equal(80, sprite["m_RD"]["m_VertexData"]["m_DataSize"].AsByteArray.Length);
            Assert.Single(sprite["m_PhysicsShape"]["Array"].Children);

            AssetTypeValueField texture = manager.GetBaseField(assets, assets.file.GetAssetsOfType(AssetClassID.Texture2D)[0]);
            Assert.Equal(6, texture["m_LightmapFormat"].AsInt);
            Assert.True(texture["m_IsAlphaChannelOptional"].AsBool);
        }
        finally
        {
            manager.UnloadAll(true);
        }
    }

    [Fact]
    public void CreateMapPackageSeed_CreatesReadableMapPackageLikeBundleWithoutTemplateAssets()
    {
        string classDataPath = FindClassDataPackage();

        byte[] bundleBytes = UnitySyntheticBundleFactory.CreateMapPackageSeed(classDataPath);

        using MemoryStream stream = new(bundleBytes, writable: false);
        AssetsManager manager = new();
        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(stream, "synthetic-map-package.bundle", unpackIfPacked: true);
            AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, 0, loadDeps: false);

            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.AssetBundle));
            Assert.Equal(2, assets.file.GetAssetsOfType(AssetClassID.MonoScript).Count);
            Assert.Equal(2, assets.file.GetAssetsOfType(AssetClassID.MonoBehaviour).Count);
            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.SpriteAtlas));
            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.Sprite));

            AssetTypeValueField assetBundle = manager.GetBaseField(assets, assets.file.GetAssetsOfType(AssetClassID.AssetBundle)[0]);
            Assert.Equal(1u, assetBundle["m_RuntimeCompatibility"].AsUInt);
            Assert.Equal(1, assetBundle["m_ExplicitDataLayout"].AsInt);

            AssetTypeValueField firstMonoBehaviour = manager.GetBaseField(assets, assets.file.GetAssetsOfType(AssetClassID.MonoBehaviour)[0]);
            AssetTypeValueField secondMonoBehaviour = manager.GetBaseField(assets, assets.file.GetAssetsOfType(AssetClassID.MonoBehaviour)[1]);

            Assert.Contains(
                [firstMonoBehaviour["m_Name"].AsString, secondMonoBehaviour["m_Name"].AsString],
                name => name == string.Empty);
            Assert.Contains(
                [firstMonoBehaviour["m_Name"].AsString, secondMonoBehaviour["m_Name"].AsString],
                name => name == "Synthetic");
        }
        finally
        {
            manager.UnloadAll(true);
        }
    }

    [Fact]
    public void CreateMapPackageSeed_CanBeConsumedByMapPackageBuilder()
    {
        string classDataPath = FindClassDataPackage();
        string tempFolder = CreateTempFolder();
        try
        {
            string syntheticSeedPath = Path.Combine(tempFolder, "synthetic-map-package.bundle");
            File.WriteAllBytes(
                syntheticSeedPath,
                UnitySyntheticBundleFactory.CreateMapPackageSeed(classDataPath));

            string outputFolder = Path.Combine(tempFolder, "output");
            string pictoTempFolder = Path.Combine(tempFolder, "pictos");
            string pictoAtlasFolder = Path.Combine(tempFolder, "atlas");
            Directory.CreateDirectory(pictoTempFolder);
            Directory.CreateDirectory(pictoAtlasFolder);

            ServerSongJSON metadata = new()
            {
                Artist = "Synthetic Artist",
                CoachCount = 1,
                Difficulty = 1,
                MapLength = 30,
                MapName = "Synthetic",
                OriginalJDVersion = 2024,
                Title = "Synthetic Song"
            };
            TimelineStructureDocument structure = new()
            {
                EndBeat = 4,
                Markers = [0, 24000, 48000, 72000, 96000]
            };
            UnityExportData data = new(
                "Synthetic",
                metadata,
                structure,
                [],
                [],
                [],
                [],
                []);

            UnityMapPackageRequest request = new(
                "Synthetic",
                data,
                [],
                pictoTempFolder,
                pictoAtlasFolder,
                MovesFolder: null,
                syntheticSeedPath,
                outputFolder,
                ForCustomServer: true);

            MapPackageBundleBuilder.Generate(request, NullLogger.Instance);

            string bundlePath = Assert.Single(Directory.GetFiles(outputFolder, "*.bundle"));
            AssetsManager manager = new();
            try
            {
                BundleFileInstance bundle = manager.LoadBundleFile(bundlePath, unpackIfPacked: true);
                AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, 0, loadDeps: false);

                Assert.Single(assets.file.GetAssetsOfType(AssetClassID.AssetBundle));
                Assert.Equal(2, assets.file.GetAssetsOfType(AssetClassID.MonoBehaviour).Count);
                Assert.Single(assets.file.GetAssetsOfType(AssetClassID.SpriteAtlas));
                Assert.Empty(assets.file.GetAssetsOfType(AssetClassID.Sprite));
            }
            finally
            {
                manager.UnloadAll(true);
            }
        }
        finally
        {
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public void MapPackageBuilder_LinksGeneratedPictoSpritesToSpriteAtlas()
    {
        string classDataPath = FindClassDataPackage();
        string tempFolder = CreateTempFolder();
        try
        {
            string syntheticSeedPath = Path.Combine(tempFolder, "synthetic-map-package.bundle");
            File.WriteAllBytes(
                syntheticSeedPath,
                UnitySyntheticBundleFactory.CreateMapPackageSeed(classDataPath));

            string outputFolder = Path.Combine(tempFolder, "output");
            string pictoTempFolder = Path.Combine(tempFolder, "pictos");
            string pictoAtlasFolder = Path.Combine(tempFolder, "atlas");
            Directory.CreateDirectory(pictoTempFolder);
            Directory.CreateDirectory(pictoAtlasFolder);

            string pictoPath = Path.Combine(pictoTempFolder, "picto_a.png");
            using (Image<Rgba32> image = new(512, 354, Color.HotPink))
                image.Save(pictoPath);

            ServerSongJSON metadata = new()
            {
                Artist = "Synthetic Artist",
                CoachCount = 2,
                Difficulty = 1,
                MapLength = 30,
                MapName = "Synthetic",
                OriginalJDVersion = 2024,
                Title = "Synthetic Song"
            };
            TimelineStructureDocument structure = new()
            {
                EndBeat = 4,
                Markers = [0, 24000, 48000, 72000, 96000]
            };
            UnityExportData data = new(
                "Synthetic",
                metadata,
                structure,
                [],
                [new PictogramClip { Id = 1, StartTime = 0, Duration = 16, PictogramId = "picto_a", CoachCount = 2 }],
                [],
                [],
                []);

            UnityMapPackageRequest request = new(
                "Synthetic",
                data,
                [pictoPath],
                pictoTempFolder,
                pictoAtlasFolder,
                MovesFolder: null,
                syntheticSeedPath,
                outputFolder,
                ForCustomServer: true);

            MapPackageBundleBuilder.Generate(request, NullLogger.Instance);

            string bundlePath = Assert.Single(Directory.GetFiles(outputFolder, "*.bundle"));
            AssetsManager manager = new();
            try
            {
                BundleFileInstance bundle = manager.LoadBundleFile(bundlePath, unpackIfPacked: true);
                AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, 0, loadDeps: false);

                AssetFileInfo spriteAtlasInfo = Assert.Single(assets.file.GetAssetsOfType(AssetClassID.SpriteAtlas));
                AssetFileInfo spriteInfo = Assert.Single(assets.file.GetAssetsOfType(AssetClassID.Sprite));
                AssetTypeValueField sprite = manager.GetBaseField(assets, spriteInfo);
                AssetTypeValueField spriteAtlas = manager.GetBaseField(assets, spriteAtlasInfo);

                Assert.Equal("picto_a", sprite["m_Name"].AsString);
                Assert.Equal(1u, sprite["m_Extrude"].AsUInt);
                Assert.Equal(512f, sprite["m_Rect"]["width"].AsFloat);
                Assert.Equal(354f, sprite["m_Rect"]["height"].AsFloat);
                Assert.Equal(0.5f, sprite["m_Pivot"]["x"].AsFloat);
                Assert.Equal(0.5f, sprite["m_Pivot"]["y"].AsFloat);
                Assert.Equal(69.140625f, sprite["m_PixelsToUnits"].AsFloat);
                Assert.Equal(0, sprite["m_SpriteAtlas"]["m_FileID"].AsInt);
                Assert.Equal(spriteAtlasInfo.PathId, sprite["m_SpriteAtlas"]["m_PathID"].AsLong);
                Assert.Equal(512f, sprite["m_RD"]["textureRect"]["width"].AsFloat);
                Assert.Equal(354f, sprite["m_RD"]["textureRect"]["height"].AsFloat);
                Assert.Equal(-1f, sprite["m_RD"]["atlasRectOffset"]["x"].AsFloat);
                Assert.Equal(-1f, sprite["m_RD"]["atlasRectOffset"]["y"].AsFloat);
                Assert.Equal(1f, sprite["m_RD"]["downscaleMultiplier"].AsFloat);
                Assert.Equal(69.140625f, sprite["m_RD"]["uvTransform"]["x"].AsFloat);
                Assert.Equal(177f, sprite["m_RD"]["uvTransform"]["w"].AsFloat);
                AssetTypeValueField channels = sprite["m_RD"]["m_VertexData"]["m_Channels"]["Array"];
                Assert.Equal(14, channels.Children.Count);
                Assert.Equal(0, channels[0]["stream"].AsByte);
                Assert.Equal(0, channels[0]["offset"].AsByte);
                Assert.Equal(0, channels[0]["format"].AsByte);
                Assert.Equal(3, channels[0]["dimension"].AsByte);
                Assert.Equal(1, channels[4]["stream"].AsByte);
                Assert.Equal(0, channels[4]["offset"].AsByte);
                Assert.Equal(0, channels[4]["format"].AsByte);
                Assert.Equal(2, channels[4]["dimension"].AsByte);
                for (int i = 1; i < channels.Children.Count; i++)
                {
                    if (i == 4)
                        continue;

                    Assert.Equal(0, channels[i]["stream"].AsByte);
                    Assert.Equal(0, channels[i]["offset"].AsByte);
                    Assert.Equal(0, channels[i]["format"].AsByte);
                    Assert.Equal(0, channels[i]["dimension"].AsByte);
                }

                byte[] vertexData = sprite["m_RD"]["m_VertexData"]["m_DataSize"].AsByteArray;
                Assert.Equal(80, vertexData.Length);
                Assert.Equal([97, 247, 108, 192], vertexData.Take(4).ToArray());
                Assert.Equal([97, 247, 108, 64], vertexData.Skip(12).Take(4).ToArray());
                Assert.All(vertexData.Skip(48), b => Assert.Equal(0, b));
                Assert.Contains(
                    spriteAtlas["m_PackedSprites"]["Array"].Children,
                    entry => entry["m_PathID"].AsLong == spriteInfo.PathId);
            }
            finally
            {
                manager.UnloadAll(true);
            }
        }
        finally
        {
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, recursive: true);
        }
    }

    private static string FindClassDataPackage()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Examples", "uabea-windows", "classdata.tpk");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find Examples/uabea-windows/classdata.tpk.");
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "JustDanceEditorUnitySyntheticTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadBundleEngineVersion(byte[] bundleBytes)
    {
        using MemoryStream stream = new(bundleBytes, writable: false);
        using BinaryReader reader = new(stream);

        Assert.Equal("UnityFS", ReadNullTerminatedAscii(reader));
        _ = ReadBigEndianInt32(reader);
        _ = ReadNullTerminatedAscii(reader);
        return ReadNullTerminatedAscii(reader);
    }

    private static string ReadNullTerminatedAscii(BinaryReader reader)
    {
        using MemoryStream buffer = new();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            byte value = reader.ReadByte();
            if (value == 0)
                break;
            buffer.WriteByte(value);
        }

        return System.Text.Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[4];
        int read = reader.Read(bytes);
        Assert.Equal(4, read);
        if (BitConverter.IsLittleEndian)
            bytes.Reverse();

        return BitConverter.ToInt32(bytes);
    }

    private static string ReadBundleCabName(byte[] bundleBytes)
    {
        using MemoryStream stream = new(bundleBytes, writable: false);
        AssetBundleFile bundle = new();
        try
        {
            bundle.Read(new AssetsFileReader(stream));
            AssetBundleDirectoryInfo directoryInfo = Assert.Single(bundle.BlockAndDirInfo.DirectoryInfos);
            return directoryInfo.Name;
        }
        finally
        {
            bundle.Close();
        }
    }
}
