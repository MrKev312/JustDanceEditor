using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public partial class PictogramCreationResult
{
    public string PictogramId { get; set; } = string.Empty;
    public int Frames { get; set; }
}

public sealed class PictogramItem : IDisposable
{
    public string Id { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public IImage? Image { get; set; }

    public void Dispose()
    {
        if (Image is IDisposable disposable)
            disposable.Dispose();
        Image = null;
    }
}

public partial class PictogramCreationViewModel : ObservableObject, IDialogResult<PictogramCreationResult>, IDisposable
{
    public IEnumerable<string> AvailablePictograms { get; } = [];
    public IEnumerable<PictogramItem> AvailablePictogramItems { get; } = [];
    public string? RootPath { get; }

    public PictogramCreationViewModel(IEnumerable<string>? pictos = null, string? rootPath = null)
    {
        AvailablePictograms = pictos ?? [];
        RootPath = rootPath;
        AvailablePictogramItems = (AvailablePictograms ?? [])
            .Select(id =>
            {
                string p = ResolveImagePath(rootPath, id);
                IImage? bmp = null;
                try
                {
                    if (!string.IsNullOrEmpty(p))
                        bmp = new Bitmap(p);
                }
                catch (Exception ex)
                {
                    EditorLog.Fallback(ex, $"Decode pictogram preview '{p}'");
                    bmp = null;
                }

                return new PictogramItem { Id = id, ImagePath = p, Image = bmp };
            })
            .ToList();
    }

    private static string ResolveImagePath(string? rootPath, string id)
    {
        if (string.IsNullOrEmpty(rootPath))
            return string.Empty;

        string path = Path.Combine(rootPath, "assets", "pictograms", id + ".webp");
        return File.Exists(path) ? path : string.Empty;
    }

    [ObservableProperty]
    public partial PictogramItem? SelectedPictogram { get; set; }

    [ObservableProperty]
    public partial decimal DurationBeats { get; set; } = 1.0M;
    public PictogramCreationResult? Result { get; private set; }

    public void Accept()
    {
        Result = new PictogramCreationResult
        {
            PictogramId = SelectedPictogram?.Id ?? string.Empty,
            Frames = (int)((double)DurationBeats * 24.0)
        };
    }

    public void Cancel() => Result = null;

    public void Dispose()
    {
        foreach (PictogramItem item in AvailablePictogramItems)
            item.Dispose();
        GC.SuppressFinalize(this);
    }
}