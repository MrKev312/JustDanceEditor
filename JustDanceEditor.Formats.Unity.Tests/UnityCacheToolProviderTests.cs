using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.Unity.Cache;
using JustDanceEditor.Formats.Unity.Tools;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;
using System.Linq;
using System.Threading;
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

    [Fact]
    public async Task ResolveOrCreateAsync_AsksInteractionWhenCacheIsMissing()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"jde-unity-cache-prompt-{Guid.NewGuid():N}");
        RecordingInteraction interaction = new("true");

        try
        {
            Directory.CreateDirectory(outputPath);
            File.WriteAllText(Path.Combine(outputPath, "not-a-cache.txt"), "placeholder");

            UnityConversionRequest request = new("/input", outputPath)
            {
                Interaction = interaction
            };

            string resolved = await UnityCacheLayout.ResolveOrCreateAsync(outputPath, request, NullLogger.Instance, TestContext.Current.CancellationToken);

            Assert.Equal(outputPath, resolved);
            Assert.Equal("unity.cache.missing", interaction.LastPromptSetId);
            Assert.Contains("likely the wrong output folder", interaction.LastPromptLabel);
            Assert.True(File.Exists(Path.Combine(outputPath, "SD_Cache.0000", "MapBaseCache", "CachingStatus.json")));
        }
        finally
        {
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, recursive: true);
        }
    }

    private sealed class RecordingInteraction(string value) : IConversionInteraction
    {
        public string? LastPromptSetId { get; private set; }

        public string LastPromptLabel { get; private set; } = string.Empty;

        public ValueTask<PromptAnswerSet> AskAsync(ConversionPromptSet promptSet, CancellationToken cancellationToken = default)
        {
            LastPromptSetId = promptSet.Id;
            LastPromptLabel = promptSet.Prompts.Single().Label;

            PromptAnswerSet answers = new();
            answers.Set(promptSet.Prompts.Single().Id, value);
            return ValueTask.FromResult(answers);
        }
    }
}