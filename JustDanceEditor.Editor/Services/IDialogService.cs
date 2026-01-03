using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public interface IDialogService
{
    Task<TResult?> ShowDialogAsync<TResult>(object viewModel);
}
