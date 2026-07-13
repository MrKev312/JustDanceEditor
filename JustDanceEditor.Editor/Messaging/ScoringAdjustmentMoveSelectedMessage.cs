using CommunityToolkit.Mvvm.Messaging.Messages;

namespace JustDanceEditor.Editor.Messaging;

public sealed class ScoringAdjustmentMoveSelectedMessage(string recordingPath, int moveIndex) : ValueChangedMessage<(string RecordingPath, int MoveIndex)>((recordingPath, moveIndex))
{
    public string RecordingPath => Value.RecordingPath;
    public int MoveIndex => Value.MoveIndex;
}