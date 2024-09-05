using JustDanceEditor.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace JustDanceEditor.Converter.Files;

public partial class FileSystem
{
    public FileSystem(ConversionRequest conversionRequest)
    {
        ConversionRequest = conversionRequest;

        // Get the song ID
        InitializeSongID();

        // Get the platform type
        InitializePlatformType();

        // Create the temporary folders
        TempFolders = new(this);
        TempFolders.CreateTempFolders();

        // Create the output folders
        OutputFolders = new(this);

        // Load the template files
        TemplateFiles = new(this);

        // Load the input folders
        InputFolders = new(this);
    }

    public ConversionRequest ConversionRequest { get; private set; }

    public string SongName { get; private set; } = "";
    public string PlatformType { get; private set; } = "";

    public TempFolders TempFolders { get; private set; }
    public InputFolders InputFolders { get; private set; }
    public OutputFolders OutputFolders { get; private set; }
    public TemplateFiles TemplateFiles { get; private set; }

    private void InitializeSongID()
    {
        if (ConversionRequest.SongName != null)
            SongName = ConversionRequest.SongName;
        else
        {
            // Get the folders from input/world/maps
            string mapsFolder = Path.Combine(ConversionRequest.InputPath, "world", "maps");
            if (!Directory.Exists(mapsFolder))
                throw new DirectoryNotFoundException("The maps folder does not exist.");

            string[] songs = Directory.GetDirectories(mapsFolder);
            if (songs.Length < 1)
                throw new DirectoryNotFoundException("No song folders found in the maps folder.");
            if (songs.Length > 1)
                throw new DirectoryNotFoundException("Multiple song folders found in the maps folder, please specify the song name in the request.");

            SongName = Path.GetFileName(songs[0]);
        }

        Logger.Log($"Song name: {SongName}", LogLevel.Important);
    }

    private void InitializePlatformType()
    {
        string itfCookedFolder = Path.Combine(ConversionRequest.InputPath, "cache", "itf_cooked");

        if (!Directory.Exists(itfCookedFolder))
            throw new DirectoryNotFoundException("The itf_cooked folder does not exist.");

        string[] platformFolders = Directory.GetDirectories(itfCookedFolder);

        if (platformFolders.Length == 0)
            throw new DirectoryNotFoundException("No platform folders found in the itf_cooked folder.");
        if (platformFolders.Length > 1)
            throw new DirectoryNotFoundException("Multiple platform folders found in the itf_cooked folder, this is not supported.");

        PlatformType = Path.GetFileName(platformFolders[0]);

        if (!PlatformType.Equals("nx", StringComparison.CurrentCultureIgnoreCase))
            Logger.Log($"Platform: {PlatformType}, which is not officially supported. The conversion might not work as expected.", LogLevel.Warning);
        else
            Logger.Log($"Platform: {PlatformType}");
    }

    public bool GetFilePath(string relativeFilePath, [MaybeNullWhen(false)] out CookedFile filePath)
    {
        filePath = null;
        string parentFolder = Path.Combine(InputFolders.InputFolder, "..");
        List<string> searchPaths = [
            Path.Combine(parentFolder, $"patch_{PlatformType}"),
            InputFolders.InputFolder,
            Path.Combine(parentFolder, $"bundle_{PlatformType}"),
            ];

        foreach (string searchPath in Directory.GetDirectories(parentFolder))
            if (!searchPaths.Contains(searchPath))
                searchPaths.Add(searchPath);

        string? pathCooked = null;

        if (Path.GetExtension(relativeFilePath) != ".ckd")
        {
            pathCooked = $"{relativeFilePath}.ckd";
        }

        // For each path, check if the file exists
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
        string[] searchPaths = Directory.GetDirectories(parentFolder);

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
            Path.Combine(parentFolder, $"bundle_{PlatformType}"),
            ];

        foreach (string searchPath in Directory.GetDirectories(parentFolder))
            if (!searchPaths.Contains(searchPath))
                searchPaths.Add(searchPath);

        // For each path, check if the folder exists
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
