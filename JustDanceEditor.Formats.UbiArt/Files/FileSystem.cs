using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Files;

public class FileSystem
{
    public FileSystem(UbiArtConversionRequest conversionRequest)
    {
        ConversionRequest = conversionRequest;

        InitializeSongID();
        InitializePlatformType();

        TempFolders = new(this);
        TempFolders.CreateTempFolders();

        InputFolders = new(this);
    }

    public UbiArtConversionRequest ConversionRequest { get; private set; }

    public string SongName { get; private set; } = "";
    public string PlatformType { get; private set; } = "";

    public TempFolders TempFolders { get; private set; }
    public InputFolders InputFolders { get; private set; }

    public void UpdateSongName(string? newSongName)
    {
        if (string.IsNullOrWhiteSpace(newSongName))
            return;

        if (string.Equals(SongName, newSongName, StringComparison.Ordinal))
            return;

        string previousTempFolder = TempFolders.MapFolder;

        SongName = newSongName;
        ConversionRequest.SongName = newSongName;

        TempFolders.CreateTempFolders();

        if (!string.Equals(previousTempFolder, TempFolders.MapFolder, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(previousTempFolder))
        {
            try
            {
                Directory.Delete(previousTempFolder, true);
            }
            catch (IOException ex)
            {
                Logger.Log($"Failed to remove old temp folder '{previousTempFolder}': {ex.Message}", LogLevel.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log($"Failed to remove old temp folder '{previousTempFolder}': {ex.Message}", LogLevel.Warning);
            }
        }
    }

    private void InitializeSongID()
    {
        if (ConversionRequest.SongName != null)
            SongName = ConversionRequest.SongName;
        else
        {
            string mapsFolder = Path.Combine(ConversionRequest.InputPath, "world", "maps");
            if (!Directory.Exists(mapsFolder))
            {
                if (ConversionRequest.Type == UbiArtType.Uncooked)
                {
                    SongName = Path.GetFileName(ConversionRequest.InputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return;
                }

                throw new DirectoryNotFoundException("The maps folder does not exist.");
            }

            string[] songs = Directory.GetDirectories(mapsFolder);
            if (songs.Length < 1)
                throw new DirectoryNotFoundException("No song folders found in the maps folder.");
            if (songs.Length > 1)
                throw new DirectoryNotFoundException("Multiple song folders found in the maps folder, please specify the song name in the request.");

            SongName = Path.GetFileName(songs[0]);
        }
    }

    private void InitializePlatformType()
    {
        string itfCookedFolder = Path.Combine(ConversionRequest.InputPath, "cache", "itf_cooked");

        if (!Directory.Exists(itfCookedFolder))
        {
            if (ConversionRequest.Type == UbiArtType.Uncooked)
            {
                PlatformType = "uncooked";
                return;
            }

            throw new DirectoryNotFoundException("The itf_cooked folder does not exist.");
        }

        string[] platformFolders = Directory.GetDirectories(itfCookedFolder);

        if (platformFolders.Length == 0)
            throw new DirectoryNotFoundException("No platform folders found in the itf_cooked folder.");
        if (platformFolders.Length > 1)
            throw new DirectoryNotFoundException("Multiple platform folders found in the itf_cooked folder, this is not supported.");

        PlatformType = Path.GetFileName(platformFolders[0]);

    }

    public bool GetFilePath(string relativeFilePath, [MaybeNullWhen(false)] out CookedFile filePath)
    {
        filePath = null;

        // For Uncooked, check the relative file path directly under InputPath first
        if (ConversionRequest.Type == UbiArtType.Uncooked)
        {
            string directChild = Path.Combine(ConversionRequest.InputPath, relativeFilePath);
            if (File.Exists(directChild))
            {
                filePath = new(directChild);
                return true;
            }

            // Check if it's world/maps/... but user pointed to the song folder
            // e.g. relativeFilePath = world/maps/songname/songdesc.tpl
            // but InputPath = .../songname
            // We can check the filename directly in InputPath
            string fileName = Path.GetFileName(relativeFilePath);
            string rootFile = Path.Combine(ConversionRequest.InputPath, fileName);
            if (File.Exists(rootFile))
            {
                filePath = new(rootFile);
                return true;
            }
        }

        string parentFolder = Path.Combine(InputFolders.InputFolder, "..");
        List<string> searchPaths = [Path.Combine(parentFolder, $"patch_{PlatformType}")];

        string[] allFolders = Directory.GetDirectories(parentFolder);
        PriorityQueue<string, uint> numberPatternFolders = new();
        List<string> otherFolders = [];

        foreach (string folder in allFolders)
        {
            if (searchPaths.Contains(folder))
                continue;

            string folderName = Path.GetFileName(folder);
            string[] parts = folderName.Split('_');

            if (parts.Length < 3)
            {
                otherFolders.Add(folder);
                continue;
            }

            string secondToLastPart = parts[^2];

            if (uint.TryParse(secondToLastPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint number))
                numberPatternFolders.Enqueue(folder, number);
            else
                otherFolders.Add(folder);
        }

        while (numberPatternFolders.Count > 0)
            searchPaths.Add(numberPatternFolders.Dequeue());

        searchPaths.AddRange(otherFolders);

        string? pathCooked = null;

        if (Path.GetExtension(relativeFilePath) != ".ckd")
            pathCooked = $"{relativeFilePath}.ckd";

        foreach (string searchPath in searchPaths)
        {
            string[] searchLocations = [
                searchPath,
                Path.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];

            foreach (string location in searchLocations)
            {
                string file = Path.Combine(location, relativeFilePath);
                if (File.Exists(file))
                {
                    filePath = new(file);
                    return true;
                }

                if (pathCooked == null)
                    continue;

                file = Path.Combine(location, pathCooked);
                if (File.Exists(file))
                {
                    filePath = new(file);
                    return true;
                }
            }
        }

        return false;
    }

    public CookedFile GetFilePath(string relativeFilePath)
    {
        return GetFilePath(relativeFilePath, out CookedFile? filePath)
            ? filePath
            : throw new FileNotFoundException($"The file {relativeFilePath} was not found in the input folders.");
    }

    public CookedFile[] GetAllFiles(string relativeFolderPath, string pattern = "*")
    {
        List<CookedFile> files = [];
        string parentFolder = Path.Combine(InputFolders.InputFolder, "..");
        List<string> searchPaths = [
            Path.Combine(parentFolder, $"patch_{PlatformType}"),
            ..Directory.GetDirectories(parentFolder)
            ];

        foreach (string searchPath in searchPaths)
        {
            string[] searchLocations = [
                searchPath,
                Path.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];

            foreach (string location in searchLocations)
            {
                string folder = Path.Combine(location, relativeFolderPath);
                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.GetFiles(folder, pattern))
                    {
                        string relative = Path.GetRelativePath(searchPath, file);
                        if (files.Any(x => x.FullPath.EndsWith(relative, StringComparison.CurrentCultureIgnoreCase)))
                            continue;

                        files.Add(new(file));
                    }
                }
            }
        }

        return [.. files];
    }

    public bool GetFolderPath(string relativeFolderPath, [MaybeNullWhen(false)] out string folderPath)
    {
        folderPath = null;
        string parentFolder = Path.Combine(InputFolders.InputFolder, "..");

        List<string> searchPaths = [
            Path.Combine(parentFolder, $"patch_{PlatformType}"),
            InputFolders.InputFolder,
            Path.Combine(parentFolder, $"bundle_{PlatformType}")
            ];

        foreach (string searchPath in Directory.GetDirectories(parentFolder))
            if (!searchPaths.Contains(searchPath))
                searchPaths.Add(searchPath);

        foreach (string searchPath in searchPaths)
        {
            string[] searchLocations = [
                searchPath,
                Path.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];
            foreach (string location in searchLocations)
            {
                string folder = Path.Combine(location, relativeFolderPath);
                if (Directory.Exists(folder))
                {
                    folderPath = folder;
                    return true;
                }
            }
        }

        return false;
    }

    public string GetFolderPath(string relativeFolderPath)
    {
        return GetFolderPath(relativeFolderPath, out string? folderPath)
            ? folderPath
            : throw new DirectoryNotFoundException($"The folder {relativeFolderPath} was not found in the input folders.");
    }

    public static string ReadWithoutNull(string filePath)
    {
        return File.ReadAllText(filePath).TrimEnd('\0');
    }
}