using JustDanceEditor.Formats.JDI.Recordings;

namespace JustDanceEditor.Formats.UbiArt.Recordings;

/// <summary>
/// Converts UbiArt REC recordings into JDI's platform-neutral motion model.
/// </summary>
public static class UbiArtMotionRecordingConverter
{
    public static int? InferCoachId(string? fileName)
        => RecMotionRecordingCodec.InferCoachId(fileName);

    public static IReadOnlyList<MotionRecordingDocument> Import(
        Stream stream,
        int firstCoachId,
        string? sourceFileName = null,
        string? songId = null)
        => RecMotionRecordingCodec.Read(stream, firstCoachId, sourceFileName, songId);
}
