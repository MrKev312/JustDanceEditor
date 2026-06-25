using JustDanceEditor.Formats.JDI.Video;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.JDI.Services;

public interface IMediaProcessor
{
    Task EncodeAudioAsync(JdiAudioEncodeRequest request, string outputPath, CancellationToken cancellationToken = default);
    Task<MemoryStream> EncodeAudioToMemoryAsync(JdiAudioEncodeRequest request, CancellationToken cancellationToken = default);
    Task<string?> GetOrCreateVideoAsync(JdiVideoEncodeRequest request, ILogger logger, CancellationToken cancellationToken = default);
}