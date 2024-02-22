using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace JustDanceEditor.Resources;
internal class VGMStream
{
	// Check whether VGMStream exists in Resources/VGMStream/ folder
	public static bool Exists() =>
		// Check whether VGMStream exists in Resources/VGMStream/ folder
		Directory.Exists("Resources/VGMStream/") && File.Exists("Resources/VGMStream/vgmstream-cli.exe");

	// Function to download the latest VGMStream release
	public static async Task Download()
	{
		// Check whether VGMStream exists in Resources/VGMStream/ folder
		if (Exists())
			return;

		// Create Resources/VGMStream/ folder
		Directory.CreateDirectory("Resources/VGMStream/");

		// Download the latest VGMStream release
		string win64 = "https://github.com/vgmstream/vgmstream/releases/latest/download/vgmstream-win64.zip";
		string win32 = "https://github.com/vgmstream/vgmstream/releases/latest/download/vgmstream-win.zip";

		string downloadURL = Environment.Is64BitOperatingSystem ? win64 : win32;

		// Use httpclient to download the latest VGMStream release
		using HttpClient client = new();
		// Download the latest VGMStream release
		using HttpResponseMessage response = await client.GetAsync(downloadURL);
		// Check whether the download was successful
		if (response.IsSuccessStatusCode)
		{
			// Download the latest VGMStream release
			using Stream stream = await response.Content.ReadAsStreamAsync();
			// Extract the latest VGMStream release
			using ZipArchive archive = new(stream);
			// Extract the latest VGMStream release
			archive.ExtractToDirectory("Resources/VGMStream/");
		}
		else
			// Throw an exception
			throw new Exception("Failed to download VGMStream, please check your internet connection.");

		// Check whether VGMStream exists in Resources/VGMStream/ folder
		if (!Exists())
			// Throw an exception
			throw new Exception("Failed to download VGMStream, please check your internet connection.");

		// Return success
		return;
	}

	// Function to check and download the latest VGMStream release
	public static async Task Check()
	{
		// Check whether VGMStream exists in Resources/VGMStream/ folder
		if (!Exists())
			// Download the latest VGMStream release
			await Download();

		// Return success
		return;
	}

	// Function to convert a file to wav
	public static async Task Convert(string input, string output)
	{
		// Check for VGMStream
		await Check();

        string vgmFullPath = Path.GetFullPath("Resources/VGMStream/vgmstream-cli.exe");

		// Create a new process
		Process process = new()
		{
			// Set the process start info
			StartInfo = new ProcessStartInfo
			{
				// Set the process start info
				FileName = vgmFullPath,
				// Set the process start info
				Arguments = $"-o \"{output}\" \"{input}\"",
				// Set the process start info
				UseShellExecute = false,
				// Set the process start info
				CreateNoWindow = true,
				// Set the process start info
				RedirectStandardOutput = true,
				// Set the process start info
				RedirectStandardError = true
            }
		};

        // Start the process
        process.Start();
		// Wait for the process to exit
		process.WaitForExit();

		// Return success
		return;
	}
}
