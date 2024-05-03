using System.Text.Json;

namespace JustDanceEditor.Helpers;
internal class Print
{
	public static void PrintCache()
	{
		string path = Question.AskFile("Enter the path to the cache file: ", true);

		// Read the file and deserialize it
		string json = File.ReadAllText(path);
		JDCacheJSON cache = JsonSerializer.Deserialize<JDCacheJSON>(json)!;

		// Print the song count
		Console.WriteLine($"Song count: {cache.MapsDict.Count}");

		// Print every song title
		foreach (KeyValuePair<string, JDSong> song in cache.MapsDict)
		{
			// { Name } - { just dance version }
			Console.WriteLine($"{song.Value.SongDatabaseEntry.Title} - {song.Value.SongDatabaseEntry.OriginalJDVersion}");
		}

		// Wait for the user to press a key
		Console.WriteLine("Press any key to exit...");
		Console.ReadKey();
	}

	internal static void PrintCacheDifferences()
	{
		string path = Question.AskFile("Enter the path to the cache file: ", true);

		// Read the file and deserialize it
		string json = File.ReadAllText(path);
		JDCacheJSON cache = JsonSerializer.Deserialize<JDCacheJSON>(json)!;
		Console.WriteLine($"Song count: {cache.MapsDict.Count}");

		// Get the folders from the same directory as the cache file
		string[] folders = Directory.GetDirectories(Path.GetDirectoryName(path)!);
		foreach (string folder in folders)
		{
			// If the foldername isn't in the cache as a key, print it
			if (!cache.MapsDict.ContainsKey(Path.GetFileName(folder)!))
			{
				Console.WriteLine(folder);
			}
		}
	}
}
