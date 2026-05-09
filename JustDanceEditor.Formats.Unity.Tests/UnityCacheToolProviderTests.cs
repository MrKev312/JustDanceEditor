using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.Unity.Tools;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityCacheToolProviderTests
{
    [Fact]
    public async Task CreateCacheTool_CreatesExpectedCacheStructure()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"jde-unity-cache-test-{Guid.NewGuid():N}");

        try
        {
            UnityCacheToolProvider provider = new(NullLogger<UnityCacheToolProvider>.Instance);
            ToolDefinition tool = provider.GetTools().Single(tool => tool.FullCode == "unity.cache-create");
            PromptAnswerSet answers = new();
            answers.Set(ConversionPromptIds.OutputPath, outputPath);

            await provider.ExecuteAsync(new ToolExecutionContext(tool, answers), TestContext.Current.CancellationToken);

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
}
