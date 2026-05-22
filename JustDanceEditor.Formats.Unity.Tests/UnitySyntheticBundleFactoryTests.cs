using AssetsTools.NET;
using AssetsTools.NET.Extra;

using System;
using System.IO;
using System.Linq;

using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.Unity.Bundles;
using JustDanceEditor.Formats.Unity.Bundles.Synthesis;
using JustDanceEditor.Formats.Unity.Models;

using KevInc.Texture;

using Microsoft.Extensions.Logging.Abstractions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public sealed class UnitySyntheticBundleFactoryTests
{
    private const string ExpectedSyntheticUnityVersion = "2021.3.40f1";

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
            TextureFormat.DXT5Crunched);

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
            TextureFormat.DXT5Crunched);

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

            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.AssetBundle));
            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.Texture2D));
            Assert.Single(assets.file.GetAssetsOfType(AssetClassID.Sprite));

            AssetTypeValueField assetBundle = manager.GetBaseField(assets, assets.file.GetAssetsOfType(AssetClassID.AssetBundle)[0]);
            Assert.Equal("Synthetic_Cover", assetBundle["m_Name"].AsString);
            Assert.Equal(2, assetBundle["m_PreloadTable"]["Array"].Children.Count);
            Assert.Equal(2, assetBundle["m_Container"]["Array"].Children.Count);
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
}
