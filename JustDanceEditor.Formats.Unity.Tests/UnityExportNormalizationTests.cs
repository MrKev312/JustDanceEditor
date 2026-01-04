using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityExportNormalizationTests
{
    [Fact]
    public void Normalize_OriginalJDVersion_To_Unity()
    {
        IntermediateSongPackage package = new() { Metadata = new IntermediateMetadata { OriginalJDVersion = 123 } };

        UnityExportData exportData = UnityExportDataBuilder.Create(package, NullLogger.Instance);
        Assert.Equal((uint)2014, exportData.Metadata.OriginalJDVersion);

        package = new() { Metadata = new IntermediateMetadata { OriginalJDVersion = 4884 } };
        exportData = UnityExportDataBuilder.Create(package, NullLogger.Instance);
        Assert.Equal((uint)2017, exportData.Metadata.OriginalJDVersion);
    }
}