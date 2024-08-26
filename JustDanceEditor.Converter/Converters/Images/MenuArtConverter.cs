using JustDanceEditor.Logging;

using System.Diagnostics;

namespace JustDanceEditor.Converter.Converters.Images;

public static class MenuArtConverter
{
    public async static Task ConvertMenuArtAsync(ConvertUbiArtToUnity convert) =>
        await Task.Run(() => ConvertMenuArt(convert));
    public static void ConvertMenuArt(ConvertUbiArtToUnity convert)
    {
        string[] menuArtFiles = Directory.GetFiles(convert.MenuArtFolder);
        Stopwatch stopwatch = Stopwatch.StartNew();

        Logger.Log($"Converting {menuArtFiles.Length} menu art files...");

        Parallel.ForEach(menuArtFiles, (item) =>
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(item);
                TextureConverter.TextureConverter.ExtractToPNG(item, Path.Combine(convert.TempMenuArtFolder, fileName + ".png"));
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to convert menu art file {item}: {ex.Message}", LogLevel.Error);
            }
        });

        stopwatch.Stop();
        Logger.Log($"Finished converting menu art files in {stopwatch.ElapsedMilliseconds}ms");
    }
}
