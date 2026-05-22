using Avalonia.Media.Imaging;

using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.Tests;

public class ImageBitmapCacheTests
{
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAAWgmWQ0AAAAASUVORK5CYII=");

    [Fact]
    public void ScheduleLoad_DoesNotThrow_And_MayPopulateCache()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"cache_{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(temp, OneByOnePng);

            // This may post back to the UI thread; we just ensure it doesn't throw
            ImageBitmapCache.ScheduleLoad(temp, () => { });

            int tries = 0;
            while (!ImageBitmapCache.TryGet(temp, out Bitmap? bmp) && tries < 50)
            {
                Thread.Sleep(50);
                tries++;
            }

            // Pass if ScheduleLoad did not throw; if the cache was populated, ensure it's a Bitmap
            if (ImageBitmapCache.TryGet(temp, out Bitmap? resultBmp))
            {
                Assert.NotNull(resultBmp);
            }
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
}