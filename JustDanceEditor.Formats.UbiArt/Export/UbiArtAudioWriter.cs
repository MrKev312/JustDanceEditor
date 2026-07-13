using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Formats.UbiArt.Export;

internal sealed class UbiArtAudioWriter
{
    public async Task WriteAsync(UbiArtExportPlan plan)
    {
        if (string.IsNullOrEmpty(plan.MaterializedRoot))
            return;

        string sourceFile = IntermediatePackageLayout.Resolve(
            plan.MaterializedRoot,
            IntermediatePackageLayout.Assets.AudioMasterFile);
        if (!plan.Context.IO.FileExists(sourceFile))
            return;

        string audioFolder = Path.Combine(plan.MapWorldBase, "audio");
        string ambienceFolder = Path.Combine(audioFolder, "amb");
        double cutSeconds = plan.Package.TimelineStructure.StartBeat < 0 &&
                            plan.Package.TimelineStructure.Markers.Count > 1
            ? -plan.Package.TimelineStructure.GetSongStartOffset()
            : 0;

        List<Task> tasks = [];
        if (cutSeconds > 0.001)
        {
            tasks.Add(plan.PlatformExporter.WriteAudioAsync(
                plan.Context,
                Path.Combine(ambienceFolder, $"amb_{plan.MapNameLower}_intro.wav"),
                new UbiArtAudioExportSource(sourceFile, Duration: TimeSpan.FromSeconds(cutSeconds))));
        }

        tasks.Add(plan.PlatformExporter.WriteAudioAsync(
            plan.Context,
            Path.Combine(audioFolder, $"{plan.MapNameLower}.wav"),
            new UbiArtAudioExportSource(
                sourceFile,
                Start: TimeSpan.FromSeconds(cutSeconds),
                Markers: plan.Package.TimelineStructure.Markers)));

        await Task.WhenAll(tasks);
    }
}
