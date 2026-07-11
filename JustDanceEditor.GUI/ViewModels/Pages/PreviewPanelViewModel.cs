using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.GUI.ViewModels.Pages;

public sealed partial class PreviewPanelViewModel : ViewModelBase, IDisposable
{
    private readonly CoverPreviewBuilder _previewBuilder;

    internal PreviewPanelViewModel(CoverPreviewBuilder previewBuilder)
    {
        _previewBuilder = previewBuilder;
    }

    [ObservableProperty]
    public partial Bitmap? CoverPreviewImage { get; set; }

    [ObservableProperty]
    public partial Bitmap? CoverPreviewSecondaryImage { get; set; }

    [ObservableProperty]
    public partial int CoverPreviewWidth { get; set; } = 512;

    [ObservableProperty]
    public partial int CoverPreviewHeight { get; set; } = 512;

    [ObservableProperty]
    public partial int CoverPreviewSecondaryWidth { get; set; } = 640;

    [ObservableProperty]
    public partial int CoverPreviewSecondaryHeight { get; set; } = 360;

    [ObservableProperty]
    public partial bool IsSingleCoverPreviewVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDualCoverPreviewVisible { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewPlaceholderVisible { get; set; } = true;

    [ObservableProperty]
    public partial string PreviewPlaceholderText { get; set; } = "Select a source to load the song.";

    [ObservableProperty]
    public partial string PreviewTitleText { get; set; } = "No song loaded";

    [ObservableProperty]
    public partial string PreviewArtistText { get; set; } = "-";

    [ObservableProperty]
    public partial string PreviewMapText { get; set; } = "Map: -";

    [ObservableProperty]
    public partial string PreviewFormatText { get; set; } = "Format: -";

    [ObservableProperty]
    public partial string PreviewStatusText { get; set; } = "Waiting";

    partial void OnCoverPreviewImageChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
            oldValue?.Dispose();
    }

    partial void OnCoverPreviewSecondaryImageChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
            oldValue?.Dispose();
    }

    public void Clear(string message)
    {
        CoverPreviewImage = null;
        CoverPreviewSecondaryImage = null;
        CoverPreviewWidth = 512;
        CoverPreviewHeight = 512;
        CoverPreviewSecondaryWidth = 640;
        CoverPreviewSecondaryHeight = 360;
        IsSingleCoverPreviewVisible = false;
        IsDualCoverPreviewVisible = false;
        PreviewPlaceholderText = message;
        IsPreviewPlaceholderVisible = true;
        PreviewTitleText = "No song loaded";
        PreviewArtistText = "-";
        PreviewMapText = "Map: -";
        PreviewFormatText = "Format: -";
        PreviewStatusText = "Waiting";
    }

    internal async Task DisplayPreviewAsync(
        JdiImportResult importResult,
        ConversionTargetDefinition? target,
        CoverPreviewOptions options,
        string sourceFormatName,
        CancellationToken cancellationToken)
    {
        CoverPreviewData preview = await _previewBuilder.BuildPreviewDataAsync(
            importResult,
            target,
            options,
            cancellationToken);

        CoverPreviewImage = preview.Bitmap;
        CoverPreviewSecondaryImage = preview.SecondaryBitmap;
        CoverPreviewWidth = preview.Width;
        CoverPreviewHeight = preview.Height;
        CoverPreviewSecondaryWidth = preview.SecondaryWidth;
        CoverPreviewSecondaryHeight = preview.SecondaryHeight;
        IsDualCoverPreviewVisible = preview.SecondaryBitmap is not null;
        IsSingleCoverPreviewVisible = !IsDualCoverPreviewVisible;
        IsPreviewPlaceholderVisible = false;
        PreviewTitleText = string.IsNullOrWhiteSpace(preview.Package.Metadata.Title)
            ? preview.Package.Metadata.MapName
            : preview.Package.Metadata.Title;
        PreviewArtistText = string.IsNullOrWhiteSpace(preview.Package.Metadata.Artist) ? "-" : preview.Package.Metadata.Artist;
        PreviewMapText = $"Map: {preview.Package.Metadata.MapName}";
        PreviewFormatText = $"Format: {sourceFormatName}";
        PreviewStatusText = preview.Package.Metadata.OriginalJDVersion > 0
            ? $"JD{preview.Package.Metadata.OriginalJDVersion}"
            : "Loaded";
    }

    public void Dispose()
    {
        CoverPreviewImage = null;
        CoverPreviewSecondaryImage = null;
    }
}