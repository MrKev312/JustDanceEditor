using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.UbiArt.Export.Ipk;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization;

using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Ipk;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Xunit;

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

    private static IntermediateSongPackage CreatePackage(string mapName, uint originalJDVersion = 0) => new()
    {
        Metadata = new IntermediateMetadata
        {
            MapName = mapName,
            Title = mapName,
            CoachCount = 1,
            OriginalJDVersion = originalJDVersion
        }
    };

    private static string CreateIpk(string archiveFolder, string fileName, IReadOnlyDictionary<string, byte[]> entries)
    {
        string sourceFolder = Path.Combine(archiveFolder, ".source_" + Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(sourceFolder);
        foreach ((string relativePath, byte[] bytes) in entries)
        {
            string path = Path.Combine(sourceFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? sourceFolder);
            File.WriteAllBytes(path, bytes);
        }

        Directory.CreateDirectory(archiveFolder);
        string archivePath = Path.Combine(archiveFolder, fileName);
        PackQuietly(sourceFolder, archivePath);
        Directory.Delete(sourceFolder, recursive: true);
        return archivePath;
    }

    private static void WriteIpkHeaderValue(string archivePath, int offset, uint value)
    {
        using FileStream stream = new(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Position = offset;
        stream.Write(bytes);
    }

    private static void PackQuietly(string sourceFolder, string archivePath)
    {
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            new UbiArtIpkWriter(sourceFolder, archivePath).Pack();
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    private static void WriteStagedFile(string stagingFolder, string relativePath, string text)
    {
        string path = Path.Combine(stagingFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? stagingFolder);
        File.WriteAllText(path, text);
    }

    private static string ReadIpkText(string archivePath, string relativePath)
        => Encoding.UTF8.GetString(ReadIpkBytes(archivePath, relativePath)).TrimEnd('\0');

    private static byte[] ReadIpkBytes(string archivePath, string relativePath)
    {
        using UbiArtIpkFileSystem fileSystem = new(archivePath);
        return fileSystem.ReadAllBytes(relativePath);
    }

    private static IReadOnlyList<string> ReadIpkEntryOrder(string archivePath)
    {
        using FileStream stream = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, Encoding.UTF8);

        byte[] magic = reader.ReadBytes(4);
        Assert.Equal(new byte[] { 0x50, 0xEC, 0x12, 0xBA }, magic);

        _ = ReadInt32BigEndian(reader);
        _ = ReadInt32BigEndian(reader);
        _ = ReadInt32BigEndian(reader);
        int filesCount = ReadInt32BigEndian(reader);

        stream.Seek(0x30, SeekOrigin.Begin);
        List<IpkEntryStrings> entries = [];
        for (int index = 0; index < filesCount; index++)
        {
            int dummy1 = ReadInt32BigEndian(reader);
            _ = ReadInt32BigEndian(reader);
            _ = ReadInt32BigEndian(reader);
            _ = ReadInt64BigEndian(reader);
            _ = ReadInt64BigEndian(reader);

            if (dummy1 == 2)
            {
                _ = ReadInt32BigEndian(reader);
                _ = ReadInt32BigEndian(reader);
            }

            string first = ReadLengthPrefixedString(reader);
            string second = ReadLengthPrefixedString(reader);
            _ = ReadInt32BigEndian(reader);
            _ = ReadInt32BigEndian(reader);
            entries.Add(new IpkEntryStrings(first, second));
        }

        bool swapPathAndName = entries.Count > 0 &&
            entries.Count(entry => entry.Second.Contains("cache/itf_cooked", StringComparison.OrdinalIgnoreCase)) > entries.Count / 2;

        return entries
            .Select(entry => ToLogicalIpkEntryPath(entry, swapPathAndName))
            .ToList();
    }

    private static string ToLogicalIpkEntryPath(IpkEntryStrings entry, bool swapPathAndName)
    {
        (string fileName, string folderPath) = swapPathAndName
            ? (entry.First, entry.Second)
            : (entry.Second.Contains('.', StringComparison.Ordinal) ? (entry.Second, entry.First) : (entry.First, entry.Second));

        string combined = string.IsNullOrEmpty(folderPath)
            ? fileName
            : $"{folderPath.TrimEnd('/', '\\')}/{fileName}";

        return UbiArtIpkArchiveIndex.NormalizePath(combined);
    }

    private static byte[] BuildXmlSkuScene(string mapName, string songDescPath)
    {
        string xml = $"""
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <root>
              <Scene>
                <ACTORS NAME="Actor">
                  <Actor USERFRIENDLY="{mapName}" LUA="{songDescPath}">
                    <COMPONENTS NAME="JD_SongDescComponent">
                      <JD_SongDescComponent />
                    </COMPONENTS>
                  </Actor>
                </ACTORS>
              </Scene>
              <JD_SongDatabaseSceneConfig>
                <CoverflowSkuSongs>
                  <CoverflowSong name="{mapName}" cover_path="world/maps/{mapName.ToLowerInvariant()}/menuart/actors/{mapName.ToLowerInvariant()}_cover_generic.act" />
                </CoverflowSkuSongs>
              </JD_SongDatabaseSceneConfig>
            </root>
            """;

        return Encoding.UTF8.GetBytes(xml);
    }

    private static byte[] BuildCarouselRulesJson(int originalJDVersion, string actionListName)
    {
        string json = $$"""
            {
              "__class": "GameConfig_CarouselRules",
              "actionLists": {},
              "rules": {
                "/party": {
                  "__class": "CarouselRule",
                  "categories": [
                    {
                      "__class": "CategoryRule",
                      "act": "ui_carousel",
                      "isc": "grp_row",
                      "title": "Just Dance {{originalJDVersion}}",
                      "titleId": 12794,
                      "requests": [
                        {
                          "__class": "JD_CarouselMapRequestDesc",
                          "actionListName": "{{actionListName}}",
                          "originalJDVersion": {{originalJDVersion}},
                          "coachCount": 0,
                          "order": "title",
                          "subscribed": false,
                          "favorites": false,
                          "sweatToggleItem": false,
                          "includedTags": [ "Main" ],
                          "excludedTags": [ "KidsOnly" ]
                        },
                        {
                          "__class": "JD_CarouselMapRequestDesc",
                          "actionListName": "{{actionListName}}",
                          "originalJDVersion": {{originalJDVersion}},
                          "coachCount": 0,
                          "order": "title",
                          "subscribed": false,
                          "favorites": false,
                          "sweatToggleItem": false,
                          "includedTags": [ "Alternate" ],
                          "excludedTags": [ "KidsOnly" ]
                        }
                      ],
                      "filters": [
                        {
                          "__class": "JD_CarouselSkuFilter",
                          "gameVersion": "",
                          "platform": ""
                        }
                      ]
                    },
                    {
                      "__class": "CategoryRule",
                      "act": "ui_carousel",
                      "isc": "grp_row",
                      "title": "Solo",
                      "titleId": 9089,
                      "requests": [
                        {
                          "__class": "JD_CarouselMapRequestDesc",
                          "actionListName": "{{actionListName}}",
                          "originalJDVersion": 0,
                          "coachCount": 1,
                          "order": "title",
                          "subscribed": false,
                          "favorites": false,
                          "sweatToggleItem": false,
                          "includedTags": [ "Main" ],
                          "excludedTags": []
                        }
                      ]
                    }
                  ],
                  "onlineOnly": false
                }
              }
            }
            """;

        return Encoding.UTF8.GetBytes(json);
    }

    private static IReadOnlyList<int> ReadCarouselVersions(string archivePath, string relativePath, string route)
    {
        using JsonDocument document = JsonDocument.Parse(ReadIpkText(archivePath, relativePath));
        JsonElement categories = document.RootElement
            .GetProperty("rules")
            .GetProperty(route)
            .GetProperty("categories");

        List<int> versions = [];
        foreach (JsonElement category in categories.EnumerateArray())
        {
            if (!category.TryGetProperty("requests", out JsonElement requests))
                continue;

            foreach (JsonElement request in requests.EnumerateArray())
            {
                if (request.TryGetProperty("__class", out JsonElement className) &&
                    string.Equals(className.GetString(), "JD_CarouselMapRequestDesc", StringComparison.Ordinal) &&
                    request.TryGetProperty("originalJDVersion", out JsonElement version) &&
                    version.TryGetInt32(out int value) &&
                    value > 0)
                {
                    versions.Add(value);
                    break;
                }
            }
        }

        return versions;
    }

    private static byte[] BuildLegacyBinarySkuSceneWithSongs(params (string MapName, string SongDescPath)[] songs)
    {
        using MemoryStream stream = new();
        using BigEndianBinaryWriter writer = new(stream);

        writer.Write(1);
        writer.Write(0x00026450u);
        for (int index = 0; index < 15; index++)
            writer.Write((byte)0);
        writer.Write((byte)(songs.Length + 1));

        WriteLegacySkuActor(writer, "skuscene_db", "world/skuscenes/skuscene_base.tpl", isSongDesc: false);
        foreach ((string mapName, string songDescPath) in songs)
            WriteLegacySkuActor(writer, mapName, songDescPath, isSongDesc: true);

        WriteLegacyCoverflowFooter(writer, songs[0].MapName);
        return stream.ToArray();
    }

    private static void WriteLegacySkuActor(
        BigEndianBinaryWriter writer,
        string name,
        string templatePath,
        bool isSongDesc)
    {
        string fileName = Path.GetFileName(templatePath.Replace('/', Path.DirectorySeparatorChar));
        string folder = templatePath[..^(fileName.Length)];
        uint resourceId = Crc32.HashToUInt32(Encoding.UTF8.GetBytes(fileName));

        writer.Write(0x97CA628Bu);
        writer.Write(0);
        writer.Write(1.0f);
        writer.Write(1.0f);
        writer.Write(0);
        writer.WriteUbiArtString(name);
        writer.Write(uint.MaxValue);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(uint.MaxValue);
        writer.Write(0);
        writer.WriteUbiArtString(fileName);
        writer.WriteUbiArtString(folder);
        writer.Write(resourceId);
        writer.Write(isSongDesc ? 2 : 0);
        writer.Write(0);
        writer.Write(1);
        writer.Write(isSongDesc ? 0xE07FCC3Fu : 0x405579FBu);
    }

    private static void WriteLegacyCoverflowFooter(BigEndianBinaryWriter writer, string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        string coverFileName = $"{mapNameLower}_cover_generic.act";
        string coverFolder = $"world/maps/{mapNameLower}/menuart/actors/";

        writer.Write(0);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0xF878DC2Du);
        writer.WriteUbiArtString("jd2020-wii-noa");
        writer.WriteUbiArtString("NCSA");
        writer.WriteUbiArtString("boot_warning_pegi.isc");
        writer.WriteUbiArtString("world/ui/screens/boot_warning/");
        writer.Write(Crc32.HashToUInt32(Encoding.UTF8.GetBytes("boot_warning_pegi.isc")));
        writer.Write(0);
        writer.Write(1);
        writer.WriteUbiArtString(mapName);
        writer.WriteUbiArtString(coverFileName);
        writer.WriteUbiArtString(coverFolder);
        writer.Write(Crc32.HashToUInt32(Encoding.UTF8.GetBytes(coverFileName)));
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
    }

    private static Dictionary<string, uint> ReadLegacySongDescResourceIds(byte[] bytes)
    {
        Dictionary<string, uint> ids = new(StringComparer.OrdinalIgnoreCase);
        int offset = 20;
        int actorCount = ReadInt32BigEndian(bytes, ref offset);

        for (int actorIndex = 0; actorIndex < actorCount; actorIndex++)
        {
            _ = ReadUInt32BigEndian(bytes, ref offset);
            offset += 4 * 4;
            string name = ReadUbiArtString(bytes, ref offset);
            offset += 4 * 8;
            string firstPathPart = ReadUbiArtString(bytes, ref offset);
            string secondPathPart = ReadUbiArtString(bytes, ref offset);
            uint resourceId = ReadUInt32BigEndian(bytes, ref offset);
            offset += 4 * 4;

            if (firstPathPart.Contains("songdesc", StringComparison.OrdinalIgnoreCase) ||
                secondPathPart.Contains("songdesc", StringComparison.OrdinalIgnoreCase))
            {
                ids[name] = resourceId;
            }
        }

        return ids;
    }

    private static SecureFatInfo ReadSecureFat(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, Encoding.UTF8);

        uint signature = unchecked((uint)ReadInt32BigEndian(reader));
        Assert.Equal(0x55534654u, signature);

        uint engineSignature = unchecked((uint)ReadInt32BigEndian(reader));
        int version = ReadInt32BigEndian(reader);
        Assert.Equal(1, version);

        int fileIdCount = ReadInt32BigEndian(reader);
        List<SecureFatFileId> fileIds = [];
        for (int index = 0; index < fileIdCount; index++)
        {
            uint fileId = unchecked((uint)ReadInt32BigEndian(reader));
            int bundleCount = ReadInt32BigEndian(reader);
            List<byte> bundleIds = [];
            for (int bundleIndex = 0; bundleIndex < bundleCount; bundleIndex++)
                bundleIds.Add(reader.ReadByte());
            fileIds.Add(new SecureFatFileId(fileId, bundleIds));
        }

        int bundleNameCount = ReadInt32BigEndian(reader);
        List<SecureFatBundle> bundles = [];
        for (int index = 0; index < bundleNameCount; index++)
        {
            byte id = reader.ReadByte();
            bundles.Add(new SecureFatBundle(id, ReadLengthPrefixedString(reader)));
        }

        return new SecureFatInfo(engineSignature, bundles, fileIds);
    }

    private static void WriteSecureFat(
        string path,
        IReadOnlyList<SecureFatFileId> fileIds,
        IReadOnlyList<SecureFatBundle> bundles)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new(stream, Encoding.UTF8);

        writer.WriteInt32BigEndian(unchecked((int)0x55534654u));
        writer.WriteInt32BigEndian(unchecked((int)0x1D3A4C30u));
        writer.WriteInt32BigEndian(1);
        writer.WriteInt32BigEndian(fileIds.Count);

        foreach (SecureFatFileId fileId in fileIds)
        {
            writer.WriteInt32BigEndian(unchecked((int)fileId.FileId));
            writer.WriteInt32BigEndian(fileId.BundleIds.Count);
            foreach (byte bundleId in fileId.BundleIds)
                writer.Write(bundleId);
        }

        writer.WriteInt32BigEndian(bundles.Count);
        foreach (SecureFatBundle bundle in bundles)
        {
            writer.Write(bundle.Id);
            writer.WriteNTString(bundle.Name);
        }
    }

    private static string ReadLengthPrefixedString(BinaryReader reader)
    {
        int length = ReadInt32BigEndian(reader);
        byte[] bytes = reader.ReadBytes(length);
        Assert.Equal(length, bytes.Length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string ReadUbiArtString(byte[] bytes, ref int offset)
    {
        int length = ReadInt32BigEndian(bytes, ref offset);
        string value = Encoding.UTF8.GetString(bytes, offset, length);
        offset += length;
        return value;
    }

    private static int ReadInt32BigEndian(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        reader.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
    }

    private static long ReadInt64BigEndian(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        reader.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt64BigEndian(bytes);
    }

    private static int ReadInt32BigEndian(byte[] bytes, ref int offset)
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    private static uint ReadUInt32BigEndian(byte[] bytes, ref int offset)
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
        offset += sizeof(uint);
        return value;
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "jde_ipk_export_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed record SecureFatInfo(uint EngineSignature, IReadOnlyList<SecureFatBundle> Bundles, IReadOnlyList<SecureFatFileId> FileIds)
    {
        public IReadOnlyList<string> BundleNames => [.. Bundles.Select(bundle => bundle.Name)];
    }

    private sealed record SecureFatFileId(uint FileId, IReadOnlyList<byte> BundleIds);

    private sealed record SecureFatBundle(byte Id, string Name);

    private sealed record IpkEntryStrings(string First, string Second);
}