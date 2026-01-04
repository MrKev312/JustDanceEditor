using JustDanceEditor.Formats.JDI.Services;

namespace JustDanceEditor.UI.Services;

internal sealed class DefaultTextureService : ITextureService
{
    public Task ConvertTextureAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        // Minimal implementation for now: copy file as a placeholder for texture conversion.
        // This can be swapped with an implementation that uses the TextureConverter project.
        File.Copy(inputPath, outputPath, overwrite: true);
        return Task.CompletedTask;
    }
}