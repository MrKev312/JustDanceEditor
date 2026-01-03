using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Editor.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public partial class PictogramCreationResult
{
    public string PictogramId { get; set; } = string.Empty;
    public int Frames { get; set; }
}

public class PictogramItem
{
    public string Id { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public IImage? Image { get; set; }
}

public partial class PictogramCreationViewModel : ObservableObject, IDialogResult<PictogramCreationResult>
{
    public IEnumerable<string> AvailablePictograms { get; } = Enumerable.Empty<string>();
    public IEnumerable<PictogramItem> AvailablePictogramItems { get; } = Enumerable.Empty<PictogramItem>();
    public string? RootPath { get; }

    public PictogramCreationViewModel(IEnumerable<string>? pictos = null, string? rootPath = null)
    {
        AvailablePictograms = pictos ?? Enumerable.Empty<string>();
        RootPath = rootPath;
        AvailablePictogramItems = (AvailablePictograms ?? Enumerable.Empty<string>())
            .Select(id =>
            {
                string p = ResolveImagePath(rootPath, id);
                IImage? bmp = null;
                try
                {
                    if (!string.IsNullOrEmpty(p))
                        bmp = new Bitmap(p);
                }
                catch { bmp = null; }

                return new PictogramItem { Id = id, ImagePath = p, Image = bmp };
            })
            .ToList();
    }

    private static string ResolveImagePath(string? rootPath, string id)
    {
        if (string.IsNullOrEmpty(rootPath))
            return string.Empty;

        string dir = Path.Combine(rootPath, "assets", "pictograms");
        string[] exts = new[] { ".png", ".webp", ".jpg", ".jpeg" };
        foreach (var ext in exts)
        {
            string p = Path.Combine(dir, id + ext);
            if (File.Exists(p))
                return p;
        }

        return string.Empty;
    }

    [ObservableProperty]
    private PictogramItem? selectedPictogram;

    [ObservableProperty]
    private decimal durationBeats = 1.0M;

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
}