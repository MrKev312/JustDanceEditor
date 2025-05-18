using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Logging;

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Converter.Converters.Images;

public static partial class MenuArtConverter
{
    public async static Task ConvertMenuArtAsync(ConversionContext context) =>
        await Task.Run(() => ConvertMenuArt(context));
    public static void ConvertMenuArt(ConversionContext context)
    {
        CookedFile[] menuArtFiles = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder);

        menuArtFiles = [.. menuArtFiles
            .OrderByDescending(f => f.IsCooked)
            .ThenBy(f => (string)f)];

        Logger.Log($"Converting {menuArtFiles.Length} menu art files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        foreach (CookedFile file in menuArtFiles)
        {
            try
            {
                CookedFile ckdFile = new(file);
                string pngPath = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, ckdFile.Name + ".png");

                // If the output file already exists, skip it
                if (File.Exists(pngPath))
                    continue;

                TextureConverter.TextureConverter.ExtractToPNG(file, pngPath);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to convert menu art file {file}: {ex.Message}", LogLevel.Error);
            }
        }

        string[] pngFiles = Directory.GetFiles(context.FileSystem.TempFolders.MenuArtFolder, "*.png");
        foreach (string pngFile in pngFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(pngFile);
            // If {song}_coachx.png or {song}_coach_x.png exists, rename it to {song}_Coach_x.png
			if (CoachMatch().IsMatch(fileName))
			{
				string newFileName = CoachMatch().Replace(fileName, "_Coach_");
				Logger.Log($"Renaming {fileName} to {newFileName}", LogLevel.Warning);
				string newFilePath = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, newFileName + ".png");
				if (!File.Exists(newFilePath))
					File.Move(pngFile, newFilePath);
                return;
			}

            // If {song}_AlbumCoach.png exists, rename it to {song}_Cover_AlbumCoach.png
            if (fileName.Contains("_AlbumCoach", StringComparison.OrdinalIgnoreCase) && !fileName.Contains("_Cover_AlbumCoach", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"Renaming {fileName} to {fileName.Replace("_AlbumCoach", "_Cover_AlbumCoach")}", LogLevel.Warning);
                string newFileName = fileName.Replace("_AlbumCoach", "_Cover_AlbumCoach", StringComparison.OrdinalIgnoreCase);
                string newFilePath = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, newFileName + ".png");
                if (!File.Exists(newFilePath))
                    File.Move(pngFile, newFilePath);
                return;
            }
        }

        stopwatch.Stop();
        Logger.Log($"Finished converting menu art files in {stopwatch.ElapsedMilliseconds}ms");
    }

    [GeneratedRegex("_coach_?")]
    private static partial Regex CoachMatch();
}
