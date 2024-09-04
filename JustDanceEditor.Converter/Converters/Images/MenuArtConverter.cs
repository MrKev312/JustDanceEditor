using JustDanceEditor.Converter.Files;
using JustDanceEditor.Logging;

using System.Diagnostics;

namespace JustDanceEditor.Converter.Converters.Images;

public static class MenuArtConverter
{
    public async static Task ConvertMenuArtAsync(ConvertUbiArtToUnity convert) =>
        await Task.Run(() => ConvertMenuArt(convert));
    public static void ConvertMenuArt(ConvertUbiArtToUnity convert)
    {
        string menuArtFolder = convert.FileSystem.GetFolderPath(convert.FileSystem.InputFolders.MenuArtFolder);

        string[] menuArtFiles = [.. Directory.GetFiles(menuArtFolder)
            .OrderByDescending(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f)];

        Logger.Log($"Converting {menuArtFiles.Length} menu art files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        Parallel.ForEach(menuArtFiles, (file) =>
        {
            try
            {
                CookedFile ckdFile = new(file);
                string pngPath = Path.Combine(convert.FileSystem.TempFolders.MenuArtFolder, ckdFile.Name + ".png");

                // If the output file already exists, skip it
                if (File.Exists(pngPath))
                    return;

                TextureConverter.TextureConverter.ExtractToPNG(file, pngPath);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to convert menu art file {file}: {ex.Message}", LogLevel.Error);
            }
        });

        stopwatch.Stop();
        Logger.Log($"Finished converting menu art files in {stopwatch.ElapsedMilliseconds}ms");
    }
}
