using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public enum UnsavedChangesChoice
{
    Cancel,
    Save,
    Discard
}

public interface IEditorPromptService
{
    Task<string?> PromptTextAsync(string title, string label, string initialValue = "");
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
    Task ShowErrorAsync(string title, string message, Exception? exception = null);
    Task<UnsavedChangesChoice> PromptUnsavedChangesAsync(string documentTitle);
}