using JustDanceEditor.Converter.Unity;
using JustDanceEditor.UI.Helpers;

using System.Text.Encodings.Web;
using System.Text.Json;

namespace JustDanceEditor.UI.Converting;

internal class GeneratorDialogue
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
}
