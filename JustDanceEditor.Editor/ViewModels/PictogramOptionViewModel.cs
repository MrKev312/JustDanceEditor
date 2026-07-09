using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using JustDanceEditor.Editor.Services;

using System;
using System.IO;

namespace JustDanceEditor.Editor.ViewModels;

public sealed class PictogramOptionViewModel : IDisposable
{
    // lazily-created red placeholder to avoid Avalonia initialization
    private static Bitmap? _redPlaceholder;

    public string Name { get; }
    public string ImagePath { get; }
    public Bitmap? PreviewImage { get; }

    public PictogramOptionViewModel(string name, string path)
    {
        Name = name;
        ImagePath = path;
        if (File.Exists(path))
        {
            try
            {
                PreviewImage = new Bitmap(path);
            }
            catch (Exception ex)
            {
                EditorLog.Fallback(ex, $"Decode pictogram preview '{path}'");
                PreviewImage = GetRedPlaceholder();
            }
        }
        else
        {
            PreviewImage = GetRedPlaceholder();
        }
    }

    private static Bitmap? GetRedPlaceholder()
    {
        if (_redPlaceholder != null)
            return _redPlaceholder;
        try
        {
            _redPlaceholder = CreateRedBitmap(32, 32);
        }
        catch (System.InvalidOperationException)
        {
            // Avalonia not initialized (e.g., in unit tests)
            // Return null; PreviewImage will be null, which is fine for tests
            return null;
        }

        return _redPlaceholder;
    }

    private static Bitmap CreateRedBitmap(int w, int h)
    {
        RenderTargetBitmap bmp = new(new Avalonia.PixelSize(w, h));
        using (DrawingContext ctx = bmp.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Red, new Rect(0, 0, w, h));
        }

        return bmp;
    }

    public override string ToString() => Name;

    public void Dispose()
    {
        if (PreviewImage != null && !ReferenceEquals(PreviewImage, _redPlaceholder))
            PreviewImage.Dispose();
        GC.SuppressFinalize(this);
    }
}
