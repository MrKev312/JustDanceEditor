using JustDanceEditor.Formats.Unity.Cache;
using JustDanceEditor.Formats.Unity.Tools;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityCacheToolProviderTests
{
    [Fact]
    public void CacheCreateTool_IsNotListed()
    {
        UnityCacheToolProvider provider = new(NullLogger<UnityCacheToolProvider>.Instance);

        Assert.DoesNotContain(provider.GetTools(), tool => tool.FullCode == "unity.cache-create");
        Assert.Contains(provider.GetTools(), tool => tool.FullCode == "unity.cache-spread");
    }

    [Fact]
    public void EnsureCacheStructure_CreatesExpectedCacheStructure()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"jde-unity-cache-test-{Guid.NewGuid():N}");

        try
        {
            UnityCacheLayout.EnsureCacheStructure(outputPath, NullLogger.Instance);

            Assert.True(File.Exists(Path.Combine(outputPath, "SD_Cache.0000", "Addressables", "json.cache")));
            Assert.True(File.Exists(Path.Combine(outputPath, "SD_Cache.0000", "MapBaseCache", "json.cache")));
            Assert.True(File.Exists(Path.Combine(outputPath, "SD_Cache.0000", "MapBaseCache", "CachingStatus.json")));
        }
        finally
        {
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public void FindCacheRoot_WalksUpFromSdCacheChild()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"jde-unity-cache-find-{Guid.NewGuid():N}");

        try
        {
            string nested = Path.Combine(outputPath, "SD_Cache.0000", "MapBaseCache", "00000000-0000-0000-0000-000000000000");
            Directory.CreateDirectory(nested);

            Assert.Equal(outputPath, UnityCacheLayout.FindCacheRoot(nested));
        }
        finally
        {
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveOrCreateAsync_UsesHeadlessCreateOverride()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"jde-unity-cache-create-{Guid.NewGuid():N}");

        try
        {
            UnityConversionRequest request = new("/input", outputPath)
            {
                GenerateCacheIfMissing = true
            };

            string resolved = await UnityCacheLayout.ResolveOrCreateAsync(outputPath, request, NullLogger.Instance, TestContext.Current.CancellationToken);

            Assert.Equal(outputPath, resolved);
            Assert.True(File.Exists(Path.Combine(outputPath, "SD_Cache.0000", "MapBaseCache", "CachingStatus.json")));
        }
        finally
        {
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, recursive: true);
        }
    }
}
