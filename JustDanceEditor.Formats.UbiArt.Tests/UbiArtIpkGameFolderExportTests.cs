using JustDanceEditor.Formats.UbiArt.Export.Ipk;
using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.UbiArtIpkGameFolderExportTestHelpers;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class UbiArtIpkGameFolderExportTests
{
    private const string WiiSkuScenePath = "cache/itf_cooked/wii/world/skuscenes/skuscene_maps_wii_noa.isc.ckd";
    private const string NxSkuScenePath = "cache/itf_cooked/nx/world/skuscenes/skuscene_maps_nx_all.isc.ckd";
    private const string NxPcSkuScenePath = "cache/itf_cooked/nx/world/skuscenes/skuscene_maps_pc_all.isc.ckd";
    private const string NxCarouselRulesPath = "cache/itf_cooked/nx/enginedata/gameconfig/gc_carousel_rules.json.ckd";

    [Fact]
    public void LooksLikeGameFolder_OnlyAcceptsFolderThatDirectlyContainsPlatformArchives()
    {
        string root = CreateTempFolder();
        try
        {
            string dataFolder = Path.Combine(root, "DATA");
            string filesFolder = Path.Combine(dataFolder, "files");
            CreateIpk(filesFolder, "bundle_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/aqueda/songdesc.tpl.ckd"] = Encoding.UTF8.GetBytes("aqueda")
            });

            Assert.False(UbiArtGameFolderIpkExporter.LooksLikeGameFolder(dataFolder, UbiArtPlatform.Revolution));
            Assert.True(UbiArtGameFolderIpkExporter.LooksLikeGameFolder(filesFolder, UbiArtPlatform.Revolution));
            Assert.False(UbiArtGameFolderIpkExporter.LooksLikeGameFolder(filesFolder, UbiArtPlatform.NX));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_RoutesPatternMatchedFilesToDominantSharedBundleWithoutDuplicating()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            const string newCoverPath = "cache/itf_cooked/wii/world/maps/newsong/menuart/actors/newsong_cover_generic.act.ckd";

            string mainBundle = CreateIpk(archives, "bundle_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/first/menuart/actors/first_cover_generic.act.ckd"] = Encoding.UTF8.GetBytes("first cover"),
                ["cache/itf_cooked/wii/world/maps/second/menuart/actors/second_cover_generic.act.ckd"] = Encoding.UTF8.GetBytes("second cover")
            });
            string logicBundle = CreateIpk(archives, "bundlelogic_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/third/menuart/actors/third_cover_generic.act.ckd"] = Encoding.UTF8.GetBytes("third cover"),
                [WiiSkuScenePath] = BuildXmlSkuScene("AQueda", "world/maps/aqueda/songdesc.tpl")
            });
            CreateIpk(archives, "bundle_0_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/first/audio/first.wav.ckd"] = Encoding.UTF8.GetBytes("audio")
            });
            WriteStagedFile(staging, newCoverPath, "new cover");
            WriteStagedFile(staging, "cache/itf_cooked/wii/world/maps/newsong/songdesc.tpl.ckd", "songdesc");

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.Revolution,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(staging, CreatePackage("NewSong"), cancellationToken: TestContext.Current.CancellationToken);

            UbiArtIpkArchiveIndex updatedMain = UbiArtIpkArchiveIndex.Read(mainBundle);
            UbiArtIpkArchiveIndex updatedLogic = UbiArtIpkArchiveIndex.Read(logicBundle);

            Assert.True(updatedMain.Contains(newCoverPath));
            Assert.False(updatedLogic.Contains(newCoverPath));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_PreservesExistingArchiveHeaderWhenRepacking()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            const string updatedPath = "cache/itf_cooked/wii/world/maps/first/songdesc.tpl.ckd";
            string bundle = CreateIpk(archives, "bundle_wii.ipk", new Dictionary<string, byte[]>
            {
                [updatedPath] = Encoding.UTF8.GetBytes("old songdesc")
            });

            WriteIpkHeaderValue(bundle, 0x08, 0x12345678u);
            WriteIpkHeaderValue(bundle, 0x20, 0x87654321u);
            WriteIpkHeaderValue(bundle, 0x24, 0xAABBCCDDu);
            WriteIpkHeaderValue(bundle, 0x28, 0x11223344u);

            WriteStagedFile(staging, updatedPath, "new songdesc");

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.Revolution,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(staging, CreatePackage("First"), cancellationToken: TestContext.Current.CancellationToken);

            UbiArtIpkArchiveIndex updated = UbiArtIpkArchiveIndex.Read(bundle);
            Assert.Equal(0x12345678u, updated.PlatformSupported);
            Assert.Equal(0x87654321u, updated.DataSignature);
            Assert.Equal(0xAABBCCDDu, updated.EngineSignature);
            Assert.Equal(0x11223344u, updated.EngineVersion);
            Assert.Equal("new songdesc", ReadIpkText(bundle, updatedPath));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_PreservesExistingArchiveEntryOrderWhenRepacking()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            const string newSongDescPath = "cache/itf_cooked/wii/world/maps/newsong/songdesc.tpl.ckd";
            string bundle = CreateIpk(archives, "bundlelogic_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/atlascontainer.ckd"] = Encoding.UTF8.GetBytes("atlas"),
                ["cache/itf_cooked/wii/sgscontainer.ckd"] = Encoding.UTF8.GetBytes("sgs"),
                [WiiSkuScenePath] = BuildXmlSkuScene("AQueda", "world/maps/aqueda/songdesc.tpl"),
                ["cache/itf_cooked/wii/world/maps/first/songdesc.tpl.ckd"] = Encoding.UTF8.GetBytes("first")
            });
            IReadOnlyList<string> originalOrder = ReadIpkEntryOrder(bundle);

            WriteStagedFile(staging, newSongDescPath, "new songdesc");

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.Revolution,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(staging, CreatePackage("NewSong"), cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlyList<string> updatedOrder = ReadIpkEntryOrder(bundle);

            Assert.Equal(originalOrder, updatedOrder.Take(originalOrder.Count));
            Assert.Equal(newSongDescPath, updatedOrder.Last(), StringComparer.OrdinalIgnoreCase);
            Assert.Contains("NewSong", ReadIpkText(bundle, WiiSkuScenePath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_RoutesPerSongGameMapContentToSongArchiveAndOnlyFrontendAssetsToSharedBundle()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            string sharedBundle = CreateIpk(archives, "bundle_nx.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/nx/world/maps/first/songdesc.tpl.ckd"] = Encoding.UTF8.GetBytes("songdesc"),
                ["cache/itf_cooked/nx/world/maps/first/songdesc.act.ckd"] = Encoding.UTF8.GetBytes("songdesc actor"),
                ["cache/itf_cooked/nx/world/maps/first/menuart/actors/first_cover_generic.act.ckd"] = Encoding.UTF8.GetBytes("cover actor"),
                ["cache/itf_cooked/nx/world/maps/first/menuart/textures/first_cover_generic.tga.ckd"] = Encoding.UTF8.GetBytes("cover texture"),
                ["cache/itf_cooked/nx/world/maps/_mashup/_mashup_main_scene.isc.ckd"] = Encoding.UTF8.GetBytes("mashup scene"),
                ["cache/itf_cooked/nx/world/maps/bornthiswayalt/timeline/pictos/crazy_ar.png.ckd"] = Encoding.UTF8.GetBytes("shared picto")
            });
            CreateIpk(archives, "first_nx.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/nx/world/maps/first/first_main_scene.isc.ckd"] = Encoding.UTF8.GetBytes("scene"),
                ["world/maps/first/videoscoach/first.vp9.720.webm"] = Encoding.UTF8.GetBytes("video")
            });

            WriteStagedFile(staging, "cache/itf_cooked/nx/world/maps/newsong/songdesc.tpl.ckd", "new songdesc");
            WriteStagedFile(staging, "cache/itf_cooked/nx/world/maps/newsong/songdesc.act.ckd", "new songdesc actor");
            WriteStagedFile(staging, "cache/itf_cooked/nx/world/maps/newsong/menuart/actors/newsong_cover_generic.act.ckd", "new cover actor");
            WriteStagedFile(staging, "cache/itf_cooked/nx/world/maps/newsong/menuart/textures/newsong_cover_generic.tga.ckd", "new cover texture");
            WriteStagedFile(staging, "cache/itf_cooked/nx/world/maps/newsong/newsong_main_scene.isc.ckd", "new scene");
            WriteStagedFile(staging, "cache/itf_cooked/nx/world/maps/newsong/timeline/pictos/crazy_ar.png.ckd", "new picto");
            WriteStagedFile(staging, "world/maps/newsong/videoscoach/newsong.vp9.720.webm", "new video");

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.NX,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(staging, CreatePackage("NewSong"), cancellationToken: TestContext.Current.CancellationToken);

            string songArchive = Path.Combine(archives, "newsong_nx.ipk");
            UbiArtIpkArchiveIndex sharedIndex = UbiArtIpkArchiveIndex.Read(sharedBundle);
            UbiArtIpkArchiveIndex songIndex = UbiArtIpkArchiveIndex.Read(songArchive);

            Assert.True(sharedIndex.Contains("cache/itf_cooked/nx/world/maps/newsong/songdesc.tpl.ckd"));
            Assert.True(sharedIndex.Contains("cache/itf_cooked/nx/world/maps/newsong/songdesc.act.ckd"));
            Assert.True(sharedIndex.Contains("cache/itf_cooked/nx/world/maps/newsong/menuart/actors/newsong_cover_generic.act.ckd"));
            Assert.True(sharedIndex.Contains("cache/itf_cooked/nx/world/maps/newsong/menuart/textures/newsong_cover_generic.tga.ckd"));
            Assert.False(sharedIndex.Contains("cache/itf_cooked/nx/world/maps/newsong/newsong_main_scene.isc.ckd"));
            Assert.False(sharedIndex.Contains("cache/itf_cooked/nx/world/maps/newsong/timeline/pictos/crazy_ar.png.ckd"));
            Assert.False(sharedIndex.Contains("world/maps/newsong/videoscoach/newsong.vp9.720.webm"));

            Assert.True(songIndex.Contains("cache/itf_cooked/nx/world/maps/newsong/newsong_main_scene.isc.ckd"));
            Assert.True(songIndex.Contains("cache/itf_cooked/nx/world/maps/newsong/timeline/pictos/crazy_ar.png.ckd"));
            Assert.True(songIndex.Contains("world/maps/newsong/videoscoach/newsong.vp9.720.webm"));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_UpdatesNxPcSkuSceneAlias()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            string bundle = CreateIpk(archives, "bundle_nx.ipk", new Dictionary<string, byte[]>
            {
                [NxSkuScenePath] = BuildXmlSkuScene("First", "world/maps/first/songdesc.tpl"),
                [NxPcSkuScenePath] = BuildXmlSkuScene("First", "world/maps/first/songdesc.tpl")
            });
            CreateIpk(archives, "first_nx.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/nx/world/maps/first/first_main_scene.isc.ckd"] = Encoding.UTF8.GetBytes("scene")
            });
            WriteStagedFile(staging, "cache/itf_cooked/nx/world/maps/newsong/songdesc.tpl.ckd", "new songdesc");

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.NX,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(staging, CreatePackage("NewSong"), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("NewSong", ReadIpkText(bundle, NxSkuScenePath), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NewSong", ReadIpkText(bundle, NxPcSkuScenePath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_UpdatesPatchSkuSceneOnlyWhenPatchOverridesBase()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            string baseBundle = CreateIpk(archives, "bundlelogic_wii.ipk", new Dictionary<string, byte[]>
            {
                [WiiSkuScenePath] = BuildXmlSkuScene("AQueda", "world/maps/aqueda/songdesc.tpl")
            });
            string patchBundle = CreateIpk(archives, "patch_wii.ipk", new Dictionary<string, byte[]>
            {
                [WiiSkuScenePath] = BuildXmlSkuScene("AQueda", "world/maps/aqueda/songdesc.tpl")
            });
            CreateIpk(archives, "bundle_0_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/aqueda/audio/aqueda.wav.ckd"] = Encoding.UTF8.GetBytes("audio")
            });
            WriteStagedFile(staging, "cache/itf_cooked/wii/world/maps/judas/songdesc.tpl.ckd", "songdesc");
            WriteStagedFile(staging, "cache/itf_cooked/wii/world/maps/judas/menuart/actors/judas_cover_generic.act.ckd", "cover");

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.Revolution,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(staging, CreatePackage("Judas"), cancellationToken: TestContext.Current.CancellationToken);

            string baseSkuScene = ReadIpkText(baseBundle, WiiSkuScenePath);
            string patchSkuScene = ReadIpkText(patchBundle, WiiSkuScenePath);

            Assert.DoesNotContain("Judas", baseSkuScene, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Judas", patchSkuScene, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_RepairsDuplicateLegacySongDescActorResourceIdsInExistingSkuScene()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            uint genericSongDescResourceId = Crc32.HashToUInt32(Encoding.UTF8.GetBytes("songdesc.main_legacy.tpl"));
            string logicBundle = CreateIpk(archives, "bundlelogic_wii.ipk", new Dictionary<string, byte[]>
            {
                [WiiSkuScenePath] = BuildLegacyBinarySkuSceneWithSongs(
                    ("AQueda", "world/maps/aqueda/songdesc.main_legacy.tpl"),
                    ("Judas", "world/maps/judas/songdesc.main_legacy.tpl"))
            });
            CreateIpk(archives, "bundle_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/aqueda/menuart/actors/aqueda_cover_generic.act.ckd"] = Encoding.UTF8.GetBytes("cover")
            });
            CreateIpk(archives, "bundle_0_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/aqueda/audio/aqueda.wav.ckd"] = Encoding.UTF8.GetBytes("audio")
            });
            WriteStagedFile(staging, "cache/itf_cooked/wii/world/maps/aqueda/songdesc.main_legacy.tpl.ckd", "songdesc");

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.Revolution,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(staging, CreatePackage("AQueda"), cancellationToken: TestContext.Current.CancellationToken);

            Dictionary<string, uint> ids = ReadLegacySongDescResourceIds(ReadIpkBytes(logicBundle, WiiSkuScenePath));

            Assert.Equal(2, ids.Count);
            Assert.NotEqual(genericSongDescResourceId, ids["AQueda"]);
            Assert.NotEqual(genericSongDescResourceId, ids["Judas"]);
            Assert.NotEqual(ids["AQueda"], ids["Judas"]);
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public void SecureFat_GroupsDuplicateFileIdsAcrossNonPatchBundles()
    {
        string root = CreateTempFolder();
        try
        {
            const string duplicatePath = "cache/itf_cooked/wii/world/maps/shared/menuart/actors/shared_cover_generic.act.ckd";
            string bundlePath = CreateIpk(root, "bundle_wii.ipk", new Dictionary<string, byte[]>
            {
                [duplicatePath] = Encoding.UTF8.GetBytes("main")
            });
            string logicPath = CreateIpk(root, "bundlelogic_wii.ipk", new Dictionary<string, byte[]>
            {
                [duplicatePath] = Encoding.UTF8.GetBytes("logic")
            });
            CreateIpk(root, "patch_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/patchonly/songdesc.tpl.ckd"] = Encoding.UTF8.GetBytes("patch")
            });
            CreateIpk(root, "Support.ipk", new Dictionary<string, byte[]>
            {
                ["DirectX/DXSETUP.exe"] = Encoding.UTF8.GetBytes("support")
            });

            uint duplicateFileId = UbiArtIpkArchiveIndex.Read(bundlePath)
                .FileIds
                .Intersect(UbiArtIpkArchiveIndex.Read(logicPath).FileIds)
                .Single();

            UbiArtSecureFatWriter.Update(root, "wii", NullLogger.Instance);

            SecureFatInfo secureFat = ReadSecureFat(Path.Combine(root, "secure_fat.gf"));
            SecureFatFileId fileId = secureFat.FileIds.Single(entry => entry.FileId == duplicateFileId);

            Assert.Equal(2, fileId.BundleIds.Count);
            Assert.Contains("bundle", secureFat.BundleNames, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("bundlelogic", secureFat.BundleNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("patch", secureFat.BundleNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("support", secureFat.BundleNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public void SecureFat_PreservesExistingFileAndBundleOrdering()
    {
        string root = CreateTempFolder();
        try
        {
            const string duplicatePath = "cache/itf_cooked/wii/world/maps/shared/menuart/actors/shared_cover_generic.act.ckd";
            string bundlePath = CreateIpk(root, "bundle_wii.ipk", new Dictionary<string, byte[]>
            {
                [duplicatePath] = Encoding.UTF8.GetBytes("main")
            });
            CreateIpk(root, "bundle_0_wii.ipk", new Dictionary<string, byte[]>
            {
                ["cache/itf_cooked/wii/world/maps/other/songdesc.tpl.ckd"] = Encoding.UTF8.GetBytes("other")
            });
            string logicPath = CreateIpk(root, "bundlelogic_wii.ipk", new Dictionary<string, byte[]>
            {
                [duplicatePath] = Encoding.UTF8.GetBytes("logic")
            });

            uint duplicateFileId = UbiArtIpkArchiveIndex.Read(bundlePath)
                .FileIds
                .Intersect(UbiArtIpkArchiveIndex.Read(logicPath).FileIds)
                .Single();

            WriteSecureFat(
                Path.Combine(root, "secure_fat.gf"),
                [
                    new SecureFatFileId(duplicateFileId, [1, 0])
                ],
                [
                    new SecureFatBundle(1, "bundle"),
                    new SecureFatBundle(24, "bundle_0"),
                    new SecureFatBundle(0, "bundlelogic")
                ]);

            UbiArtSecureFatWriter.Update(root, "wii", NullLogger.Instance);

            SecureFatInfo secureFat = ReadSecureFat(Path.Combine(root, "secure_fat.gf"));
            SecureFatFileId duplicateEntry = secureFat.FileIds.Single(entry => entry.FileId == duplicateFileId);

            Assert.Equal(new byte[] { 1, 0 }, duplicateEntry.BundleIds);
            Assert.Equal(new[] { "bundle", "bundle_0", "bundlelogic" }, secureFat.Bundles.Select(bundle => bundle.Name));
            Assert.Equal(new byte[] { 1, 24, 0 }, secureFat.Bundles.Select(bundle => bundle.Id));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_AddsMissingModernCarouselRuleToPatchOwner()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            string baseBundle = CreateIpk(archives, "bundle_nx.ipk", new Dictionary<string, byte[]>
            {
                [NxCarouselRulesPath] = BuildCarouselRulesJson(2018, "partyMap")
            });
            string patchBundle = CreateIpk(archives, "patch_nx.ipk", new Dictionary<string, byte[]>
            {
                [NxCarouselRulesPath] = BuildCarouselRulesJson(2018, "partyMap")
            });

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.NX,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(
                staging,
                CreatePackage("FutureSong", originalJDVersion: 2021),
                UbiArtEngineVersion.JD2018,
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain(2021, ReadCarouselVersions(baseBundle, NxCarouselRulesPath, "/party"));
            Assert.Contains(2021, ReadCarouselVersions(patchBundle, NxCarouselRulesPath, "/party"));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_AddsMissingModernCarouselRuleToAllBaseOwnersWhenNoPatchOverrideExists()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            string bundle = CreateIpk(archives, "bundle_nx.ipk", new Dictionary<string, byte[]>
            {
                [NxCarouselRulesPath] = BuildCarouselRulesJson(2018, "partyMap")
            });
            string logicBundle = CreateIpk(archives, "bundlelogic_nx.ipk", new Dictionary<string, byte[]>
            {
                [NxCarouselRulesPath] = BuildCarouselRulesJson(2018, "partyMap")
            });

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.NX,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(
                staging,
                CreatePackage("FutureSong", originalJDVersion: 2021),
                UbiArtEngineVersion.JD2018,
                TestContext.Current.CancellationToken);

            Assert.Contains(2021, ReadCarouselVersions(bundle, NxCarouselRulesPath, "/party"));
            Assert.Contains(2021, ReadCarouselVersions(logicBundle, NxCarouselRulesPath, "/party"));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_DoesNotDuplicateExistingModernCarouselRule()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            string bundle = CreateIpk(archives, "bundle_nx.ipk", new Dictionary<string, byte[]>
            {
                [NxCarouselRulesPath] = BuildCarouselRulesJson(2018, "partyMap")
            });
            Directory.CreateDirectory(staging);

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.NX,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(
                staging,
                CreatePackage("CurrentSong", originalJDVersion: 2018),
                UbiArtEngineVersion.JD2018,
                TestContext.Current.CancellationToken);

            Assert.Equal(1, ReadCarouselVersions(bundle, NxCarouselRulesPath, "/party").Count(version => version == 2018));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }

    [Fact]
    public async Task ApplyAsync_DoesNotAddCarouselRuleForTargetGameVersion()
    {
        string root = CreateTempFolder();
        string staging = Path.Combine(root, "staging");
        string archives = Path.Combine(root, "archives");
        try
        {
            string bundle = CreateIpk(archives, "bundle_nx.ipk", new Dictionary<string, byte[]>
            {
                [NxCarouselRulesPath] = BuildCarouselRulesJson(2017, "partyMap")
            });
            Directory.CreateDirectory(staging);

            bool created = UbiArtGameFolderIpkExporter.TryCreate(
                archives,
                UbiArtPlatform.NX,
                NullLogger.Instance,
                out UbiArtGameFolderIpkExporter? exporter);

            Assert.True(created);
            Assert.NotNull(exporter);

            await exporter.ApplyAsync(
                staging,
                CreatePackage("CurrentSong", originalJDVersion: 2018),
                UbiArtEngineVersion.JD2018,
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain(2018, ReadCarouselVersions(bundle, NxCarouselRulesPath, "/party"));
        }
        finally
        {
            DeleteTempFolder(root);
        }
    }
}