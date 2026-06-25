using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.Unity.Services;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public sealed class UnityJdiFormatTests
{
    [Fact]
    public async Task ImportAsync_WhenOutputParentIsInputParent_RedirectsMaterializedRootAndPreservesSource()
    {
        string parentRoot = CreateTempDirectory();
        string inputRoot = Path.Combine(parentRoot, "makeba");
        string? materializedRoot = null;

        try
        {
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "SongInfo.json"), "{}");
            File.WriteAllText(Path.Combine(inputRoot, "source.keep"), "keep");

            RecordingMaterializer materializer = new();
            UnityJdiFormat format = new(
                _ => CreatePackage("makeba"),
                materializer,
                NullLogger<UnityJdiFormat>.Instance);

            JdiImportResult result = await format.ImportAsync(
                new UnityConversionRequest(inputRoot, parentRoot),
                TestContext.Current.CancellationToken);

            materializedRoot = result.MaterializedRoot;
            Assert.NotNull(materializedRoot);
            Assert.False(MaterializedOutputPathResolver.PathsOverlap(inputRoot, materializedRoot));
            Assert.Equal(materializedRoot, materializer.TargetRoot);
            Assert.True(File.Exists(Path.Combine(materializedRoot, "metadata.json")));
            Assert.True(File.Exists(Path.Combine(inputRoot, "SongInfo.json")));
            Assert.True(File.Exists(Path.Combine(inputRoot, "source.keep")));
        }
        finally
        {
            if (Directory.Exists(parentRoot))
                Directory.Delete(parentRoot, recursive: true);

            if (!string.IsNullOrWhiteSpace(materializedRoot) && Directory.Exists(materializedRoot))
                Directory.Delete(materializedRoot, recursive: true);
        }
    }

    private static IntermediateSongPackage CreatePackage(string mapName) =>
        new()
        {
            Metadata = new IntermediateMetadata
            {
                MapName = mapName,
                ParentMapName = mapName,
                Title = "Makeba",
                Artist = "Test Artist",
                CoachCount = 1,
                OriginalJDVersion = 2024
            }
        };

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "JustDanceEditor.Unity.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingMaterializer : IUnityAssetMaterializer
    {
        public string? TargetRoot { get; private set; }

        public void Materialize(IntermediateSongPackage package, string unityRoot, string targetRoot)
        {
            TargetRoot = targetRoot;
            Directory.CreateDirectory(targetRoot);
            File.WriteAllText(Path.Combine(targetRoot, "materialized.asset"), unityRoot);
        }
    }
}