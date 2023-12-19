using System.Security.Cryptography;

namespace JustDanceEditor.Helpers;
internal static class Download
{
    /// <summary>
    /// Downloads a file from a URL to a folder and renames it to the MD5 hash of the file.
    /// </summary>
    /// <returns>The MD5 hash of the file.</returns>
    public static string DownloadFileMD5(string url, string folderPath, bool keepExtension = false, bool errorIfFileExists = false)
    {
        // Create the destination folder if it doesn't exist
        Directory.CreateDirectory(folderPath);

        // Get the extension of the file
        Uri uri = new(url);
        string fileName = uri.Segments.Last();
        string extension = "";

        if (keepExtension)
            extension = Path.GetExtension(fileName);

        // Download the file as "temp"
        string tempFilePath = Path.Combine(folderPath, "temp" + extension);

        using HttpClient client = new();
        using Stream stream = client.GetStreamAsync(url).Result;
        using FileStream fileStream = File.Create(tempFilePath);
        stream.CopyTo(fileStream);

        // Close the file stream so we can get the MD5 hash of the file
        fileStream.Close();

        // Get the MD5 hash of the file
        string hashString = GetFileMD5(tempFilePath);

        // Rename the file to the MD5 hash
        string filePath = Path.Combine(folderPath, hashString + extension);

        if (File.Exists(filePath))
        {
            if (errorIfFileExists)
                throw new Exception($"The file {fileName} already exists.");

            Console.WriteLine($"The file {fileName} already exists, skipping.");
            return hashString;
        }

        File.Move(tempFilePath, filePath);

        return hashString;
    }

    public static string GetFileMD5(string filePath)
    {
        byte[] hash = MD5.HashData(File.ReadAllBytes(filePath));

        // Convert the byte array to a hex string
        string hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        return hashString;
    }

    public static void DownloadFile(string url, string folderPath, bool keepExtension = true, bool errorIfFileExists = false)
    {
        Uri uri = new(url);
        string fileName = uri.Segments.Last();

        fileName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        if (keepExtension)
            fileName += extension;

        string filePath = Path.Combine(folderPath, fileName);

        if (File.Exists(filePath))
        {
            if (errorIfFileExists)
                throw new Exception($"The file {fileName} already exists.");

            Console.WriteLine($"The file {fileName} already exists, skipping.");
            return;
        }

        Directory.CreateDirectory(folderPath);

        using HttpClient client = new();
        using Stream stream = client.GetStreamAsync(url).Result;
        using FileStream fileStream = File.Create(filePath);
        stream.CopyTo(fileStream);
    }
}
