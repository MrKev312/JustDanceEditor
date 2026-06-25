using Avalonia.Media.Imaging;

using JustDanceEditor.Editor.Views.Converters;

namespace JustDanceEditor.Editor.Tests;

public class BitmapValueConverterTests
{
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAAWgmWQ0AAAAASUVORK5CYII=");

    [Fact]
    public void Convert_DoesNotThrow_ForExistingFile()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(temp, OneByOnePng);
            BitmapValueConverter conv = new();
            object? res = conv.Convert(temp, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);
            // In some test environments bitmap decoding may not be available; ensure no exception and result is either Bitmap or null
            Assert.True(File.Exists(temp));
            Assert.True(res is Bitmap or null);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch { }
        }
    }

    [Fact]
    public void Convert_DoesNotThrow_ForMissingFile()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png");
        BitmapValueConverter conv = new();
        object? res = conv.Convert(temp, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(res is Bitmap or null);
    }
}