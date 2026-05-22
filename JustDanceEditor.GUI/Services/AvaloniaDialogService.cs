using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.GUI.ViewModels;

namespace JustDanceEditor.GUI.Services;

public sealed class AvaloniaDialogService : IApplicationDialogService
{
    public async Task<string?> PickFileAsync(string title, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Window owner = GetMainWindow();
            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            cancellationToken.ThrowIfCancellationRequested();
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        });
    }

    public async Task<string?> PickSaveFileAsync(string title, string? suggestedFileName = null, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Window owner = GetMainWindow();
            IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName
            });

            cancellationToken.ThrowIfCancellationRequested();
            return file?.TryGetLocalPath();
        });
    }

    public async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Window owner = GetMainWindow();
            IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            cancellationToken.ThrowIfCancellationRequested();
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        });
    }

    public async Task<bool> AskBooleanAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Window owner = GetMainWindow();
            Window dialog = CreatePromptWindow(title);

            TextBlock messageBlock = new()
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            };

            Button cancelButton = new() { Content = "Cancel", MinWidth = 92 };
            Button continueButton = new()
            {
                Content = "Continue",
                Classes = { "primary" },
                MinWidth = 104
            };

            cancelButton.Click += (_, _) => dialog.Close(false);
            continueButton.Click += (_, _) => dialog.Close(true);

            dialog.Content = new StackPanel
            {
                Margin = new Thickness(18),
                Children =
                {
                    messageBlock,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, continueButton }
                    }
                }
            };

            using CancellationTokenRegistration registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
            return await dialog.ShowDialog<bool>(owner);
        });
    }

    public async Task<string?> AskChoiceAsync(string title, string message, IReadOnlyList<PromptOption> options, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Window owner = GetMainWindow();
            Window dialog = CreatePromptWindow(title);
            ComboBox comboBox = new()
            {
                ItemsSource = options.Select(option => new PromptOptionItemViewModel(option)).ToArray(),
                SelectedIndex = options.Count > 0 ? 0 : -1,
                MinWidth = 320
            };

            Button cancelButton = new() { Content = "Cancel", MinWidth = 92 };
            Button continueButton = new()
            {
                Content = "Continue",
                Classes = { "primary" },
                MinWidth = 104
            };

            cancelButton.Click += (_, _) => dialog.Close(null);
            continueButton.Click += (_, _) =>
            {
                string? value = comboBox.SelectedItem is PromptOptionItemViewModel item ? item.Option.Value : null;
                dialog.Close(value);
            };

            dialog.Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    comboBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, continueButton }
                    }
                }
            };

            using CancellationTokenRegistration registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(null)));
            return await dialog.ShowDialog<string?>(owner);
        });
    }

    public void CloseMainWindow()
    {
        Dispatcher.UIThread.Post(() => GetMainWindow().Close());
    }

    private static Window GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is not null)
            return desktop.MainWindow;

        throw new InvalidOperationException("The main window is not available.");
    }

    private static Window CreatePromptWindow(string title) =>
        new()
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#2A2433"))
        };
}
