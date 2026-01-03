namespace JustDanceEditor.Editor.Services;

public interface IDialogResult<TResult>
{
    TResult? Result { get; }
}