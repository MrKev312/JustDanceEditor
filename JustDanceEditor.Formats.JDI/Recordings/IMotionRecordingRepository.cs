namespace JustDanceEditor.Formats.JDI.Recordings;

public interface IMotionRecordingRepository
{
    Task<string> SaveAsync(string packageRoot, MotionRecordingDocument recording, CancellationToken cancellationToken = default);
    Task<MotionRecordingDocument> LoadAsync(string path, CancellationToken cancellationToken = default);
    IReadOnlyList<string> ListRecordingFiles(string packageRoot, int? coachId = null);
}