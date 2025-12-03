using JustDanceEditor.Formats.Unity;
using JustDanceEditor.Logging;
using JustDanceEditor.UI.Helpers;

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JustDanceEditor.UI.Converting;

internal class CacheDialogue
{
    static readonly JsonSerializerOptions options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static void GenerateCacheDialogue()
    {
        Console.WriteLine("This option will create a new, empty cache structure in the specified directory.");
        string path = Question.AskFolder("Please enter the full path where you want the new cache to be saved");

        try
        {
            Directory.CreateDirectory(path);

            // In it create a SD_Cache.0000 folder
            string cachePath = Path.Combine(path, "SD_Cache.0000");
            string addressablesPath = Path.Combine(cachePath, "Addressables");
            string mapBaseCachePath = Path.Combine(cachePath, "MapBaseCache");

            Directory.CreateDirectory(addressablesPath);
            Directory.CreateDirectory(mapBaseCachePath);

            // Create addresables's json.cache
            string addressablesJsonCachePath = Path.Combine(addressablesPath, "json.cache");
            File.WriteAllText(addressablesJsonCachePath, JDSongFactory.AddressablesJson());
            Console.WriteLine($"Created: {addressablesJsonCachePath}");

            // Create mapbasecache's json.cache
            string mapBaseCacheJsonCachePath = Path.Combine(mapBaseCachePath, "json.cache");
            File.WriteAllText(mapBaseCacheJsonCachePath, JDSongFactory.MapBaseCacheJson());
            Console.WriteLine($"Created: {mapBaseCacheJsonCachePath}");

            // Get a blank cachingStatus json
            string cachingStatusJsonPath = Path.Combine(mapBaseCachePath, "CachingStatus.json");
            JDCacheJSON jDCacheJSON = new();
            File.WriteAllText(cachingStatusJsonPath, JsonSerializer.Serialize(jDCacheJSON, options));
            Console.WriteLine($"Created: {cachingStatusJsonPath}");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nNew cache structure generated successfully!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred while generating the cache: {ex.Message}");
            Logger.Log($"Error generating cache: {ex.Message}", LogLevel.Error);
            Console.ResetColor();
        }
    }

    public static void SpreadCacheDialogue()
    {
        Console.WriteLine("This option reorganizes cache folders as exFAT can have bigger cache folders");
        Console.WriteLine("for when the game's cache limit is approached (around SD_Cache.002A).");
        Console.WriteLine("Ensure you have a backup of your cache before proceeding.");

        // Ask for the cache folder
        string cachePath = Question.AskFolder("Please enter the full path to your cache folder", true);

        // Check if it has a SD_Cache.0000 folder
        if (!Directory.Exists(Path.Combine(cachePath, "SD_Cache.0000")))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: The specified folder doesn't appear to be a valid cache. Missing 'SD_Cache.0000'.");
            Console.ResetColor();
            return;
        }

        // Check if it has a SD_Cache.002A folder
        if (!Directory.Exists(Path.Combine(cachePath, "SD_Cache.002A")))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: Cache folder 'SD_Cache.002A' not found. Spreading might not be necessary yet.");
            Console.ResetColor();
            if (!Question.AskYesNo("Do you want to continue anyway?"))
                return;
        }

        // Load the cachingStatus.json
        string cachingStatusJsonPath = Path.Combine(cachePath, "SD_Cache.0000", "MapBaseCache", "CachingStatus.json");

        if (!File.Exists(cachingStatusJsonPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: 'CachingStatus.json' not found in the cache. Cannot proceed.");
            Console.ResetColor();
            return;
        }

        try
        {
            Console.WriteLine("\nAnalyzing cache structure...");
            JDCacheJSON jDCacheJSON = JsonSerializer.Deserialize<JDCacheJSON>(File.ReadAllText(cachingStatusJsonPath))!;

            // Get every folder in the cache folder
            string[] folders = Directory.GetDirectories(cachePath);
            PriorityQueue<string, long> cacheOutputFolderSizes = new();
            List<string> cacheInputFolderSizes = [];

            foreach (string folder in folders)
            {
                string folderName = Path.GetFileName(folder);

                // Skip the SD_Cache.0000 folder
                if (folderName == "SD_Cache.0000")
                    continue;

                // Only add the SD_Cache folders
                if (!folderName.StartsWith("SD_Cache."))
                    continue;

                if (cacheOutputFolderSizes.Count < 0x29) // Limit to SD_Cache.0000 to SD_Cache.0028 (41 folders)
                    cacheOutputFolderSizes.Enqueue(folderName, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(t => new FileInfo(t).Length));
                else
                    cacheInputFolderSizes.Add(folderName);
            }

            if (cacheInputFolderSizes.Count == 0)
            {
                Console.WriteLine("No folders found beyond the initial 0x29 cache folders. Spreading is not needed.");
                return;
            }

            Console.WriteLine("\nFolders to be spread:");
            foreach (string folder in cacheInputFolderSizes)
            {
                Console.WriteLine($" - Source: {folder}");

                string fullFolder = Path.Combine(cachePath, folder);
                string[] songFolders = Directory.GetDirectories(fullFolder);

                foreach (string songFolder in songFolders)
                {
                    Guid songFolderName = Guid.Parse(Path.GetFileName(songFolder));

                    JDSong jDSong = jDCacheJSON.MapsDict[songFolderName];

                    if (!cacheOutputFolderSizes.TryDequeue(out string? songFolderOutput, out long priority))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Error: Ran out of target cache folders to spread into. This shouldn't happen if limits are correct.");
                        Console.ResetColor();
                        throw new Exception("The cacheOutputFolderSizes is empty during spread operation.");
                    }

                    uint folderNumber = uint.Parse(songFolderOutput[^4..], NumberStyles.HexNumber);

                    Console.WriteLine($"   - Moving song '{songFolderName}' to '{songFolderOutput}' (Cache {folderNumber:X4})");

                    // First update the CachingStatus.json
                    JDSongFactory.UpdateSong(jDSong, folderNumber);
                    jDCacheJSON.MapsDict[songFolderName] = jDSong;

                    // Get the size of the song folder and re-enqueue the target folder with updated size
                    long songFolderSize = Directory.GetFiles(songFolder, "*", SearchOption.AllDirectories).Sum(t => new FileInfo(t).Length);
                    cacheOutputFolderSizes.Enqueue(songFolderOutput, priority + songFolderSize);

                    string fullSongFolderOutput = Path.Combine(cachePath, songFolderOutput, songFolderName.ToString());
                    Directory.Move(songFolder, fullSongFolderOutput);

                    // Overwrite the json.cache
                    string jsonCachePath = Path.Combine(fullSongFolderOutput, "json.cache");
                    string jsonCache = JDSongFactory.CacheJson(folderNumber, songFolderName);
                    File.WriteAllText(jsonCachePath, jsonCache);
                }

                Directory.Delete(fullFolder);
                Console.WriteLine($" - Deleted empty source folder: {folder}");
            }

            // Duplicate the CachingStatus.json to CachingStatus.json.bak
            string backupPath = cachingStatusJsonPath + ".bak";
            File.Copy(cachingStatusJsonPath, backupPath, true);
            Console.WriteLine($"\nBacked up 'CachingStatus.json' to '{backupPath}'");

            // Save the CachingStatus.json
            File.WriteAllText(cachingStatusJsonPath, JsonSerializer.Serialize(jDCacheJSON, options));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Cache spreading completed successfully!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred during cache spreading: {ex.Message}");
            Logger.Log($"Error spreading cache: {ex.Message}", LogLevel.Error);
            Console.ResetColor();
        }
    }
}