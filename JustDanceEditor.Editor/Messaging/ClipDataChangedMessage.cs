using CommunityToolkit.Mvvm.Messaging.Messages;

namespace JustDanceEditor.Editor.Messaging;

public sealed class ClipDataChangedMessage(object source, string? propertyName) : ValueChangedMessage<(object Source, string? PropertyName)>((source, propertyName))
{
    public object Source => Value.Source;
    public string? PropertyName => Value.PropertyName;
}