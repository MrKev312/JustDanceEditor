using JustDanceEditor.Converter.Unity;
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
        string path = Question.AskFolder("Where do you want the new cache to be saved");

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

        // Create mapbasecache's json.cache
        string mapBaseCacheJsonCachePath = Path.Combine(mapBaseCachePath, "json.cache");
        File.WriteAllText(mapBaseCacheJsonCachePath, JDSongFactory.MapBaseCacheJson());

        // Get a blank cachingStatus json
        string cachingStatusJsonPath = Path.Combine(mapBaseCachePath, "CachingStatus.json");
        JDCacheJSON jDCacheJSON = new();
        File.WriteAllText(cachingStatusJsonPath, JsonSerializer.Serialize(jDCacheJSON, options));
    }

    public static void SpreadCacheDialogue()
    {
        // Ask for the cache folder
        string cachePath = Question.AskFolder("Where is the cache folder", true);

        // Check if it has a SD_Cache.0000 folder
        if (!Directory.Exists(Path.Combine(cachePath, "SD_Cache.0000")))
        {
            Console.WriteLine("The folder doesn't have a SD_Cache.0000 folder");
            return;
        }

        // Check if it has a SD_Cache.002A folder
        if (!Directory.Exists(Path.Combine(cachePath, "SD_Cache.002A")))
        {
            Console.WriteLine("You're not at the cache limit yet, you don't need to spread the caches");
            return;
        }

        // Load the cachingStatus.json
        string cachingStatusJsonPath = Path.Combine(cachePath, "SD_Cache.0000", "MapBaseCache", "CachingStatus.json");

        if (!File.Exists(cachingStatusJsonPath))
        {
            Console.WriteLine("The cache doesn't have a CachingStatus.json file");
            return;
        }

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

            if (cacheOutputFolderSizes.Count < 0x29)
                cacheOutputFolderSizes.Enqueue(folderName, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(t => new FileInfo(t).Length));
            else
                cacheInputFolderSizes.Add(folderName);
        }

        // Print the folders
        Console.WriteLine("Folders to spread:");
        foreach (string folder in cacheInputFolderSizes)
        {
            Console.WriteLine($" - {folder}");

            string fullFolder = Path.Combine(cachePath, folder);
            string[] songFolders = Directory.GetDirectories(fullFolder);

            foreach (string songFolder in songFolders)
            {
                string songFolderName = Path.GetFileName(songFolder);

                JDSong jDSong = jDCacheJSON.MapsDict[songFolderName];

                if (!cacheOutputFolderSizes.TryDequeue(out string? songFolderOutput, out long priority))
                    throw new Exception("The cacheOutputFolderSizes is empty");

                // Get the last 4 characters of the folder name and convert it to an int
                uint folderNumber = uint.Parse(songFolderOutput[^4..], NumberStyles.HexNumber);

                Console.WriteLine($"   - {songFolderName} -> {songFolderOutput}");

                // First update the CachingStatus.json
                JDSongFactory.UpdateSong(jDSong, folderNumber);
                jDCacheJSON.MapsDict[songFolderName] = jDSong;

                // Get the size of the song folder
                long songFolderSize = Directory.GetFiles(songFolder, "*", SearchOption.AllDirectories).Sum(t => new FileInfo(t).Length);
                cacheOutputFolderSizes.Enqueue(songFolderOutput, priority + songFolderSize);
                string fullSongFolderOutput = Path.Combine(cachePath, songFolderOutput, songFolderName);
                Directory.Move(songFolder, fullSongFolderOutput);

                // Overwrite the json.cache
                string jsonCachePath = Path.Combine(fullSongFolderOutput, "json.cache");
                string jsonCache = JDSongFactory.CacheJson(folderNumber, songFolderName);
                File.WriteAllText(jsonCachePath, jsonCache);
            }

            Directory.Delete(fullFolder);
        }

        // Duplicate the CachingStatus.json to CachingStatus.json.bak
        File.Copy(cachingStatusJsonPath, cachingStatusJsonPath + ".bak", true);

        // Save the CachingStatus.json
        File.WriteAllText(cachingStatusJsonPath, JsonSerializer.Serialize(jDCacheJSON, options));
    }
}
