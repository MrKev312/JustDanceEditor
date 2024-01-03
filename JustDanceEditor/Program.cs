using System.Reflection;

using JustDanceEditor.Helpers;
using JustDanceEditor.JD2Next;

namespace JustDanceEditor;

internal class Program
{
	static void Main()
	{
		Console.WriteLine("Just Dance Editor");
		Console.WriteLine("Made by: MrKev312");
		Console.WriteLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version}");
		MainLoop();
		Console.WriteLine("Exiting...");
	}

	private static void MainLoop()
	{
		while (true)
		{
			int choice = Question.Ask([
				"Exit",
				"Cache stuff",
				"Convert UbiArt to Unity"
			]);

			switch (choice)
			{
				case 0:
					return;
				case 1:
					CacheStuff();
					break;
				case 2:
					ConvertUbiArtToUnity.Convert();
					break;
			}
		}
	}

	private static void CacheStuff()
	{
		int choice = Question.Ask([
			"Exit",
			"Print cache information",
			"Cache generation stuff",
			"See which folders differ from the cache"
		]);

		switch (choice)
		{

			case 0:
				return;
			case 1:
				Print.PrintCache();
				break;
			case 2:
				GenerateCache();
				break;
			case 3:
				Print.PrintCacheDifferences();
				break;
		}
	}

	private static void GenerateCache()
	{
		// Ask if the user wants to generate a song with existing data or URLs
		int choice = Question.Ask([
			"Exit",
			"Generate a song with URLs (manual data)",
			"Generate a song with URLs + songdb",
			"Generate a song with existing data (jd-s3.cdn.ubi.com + json)",
			"Convert Cache0/x to proper cache"
		]);

		switch (choice)
		{
			case 0:
				return;
			case 1:
				GenerateCacheURL.GenerateCacheWithUrls();
				break;
			case 2:
				GenerateCacheURL.GenerateCacheWithSongDBAndUrls();
				break;
			case 3:
				GenerateCacheURL.GenerateCacheWithExistingData();
				break;
			case 4:
				GenerateCacheURL.ConvertToProperCache();
				break;
		}
	}
}