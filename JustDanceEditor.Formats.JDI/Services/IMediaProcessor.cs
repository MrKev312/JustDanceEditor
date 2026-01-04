namespace JustDanceEditor.Formats.JDI.Services;

public interface IMediaProcessor
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
    Task ConvertAsync(string input, string output, string[]? extraArgs = null, CancellationToken cancellationToken = default);
}