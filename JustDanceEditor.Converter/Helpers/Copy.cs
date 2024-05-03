namespace JustDanceEditor.Converter.Helpers;

public static class Copy
{
    public static uint CopyFolder(string sourceFolder, string destFolder)
    {
        uint bytesCopied = 0;

        // Check if the source folder exists
        if (!Directory.Exists(sourceFolder))
            throw new Exception($"The folder {sourceFolder} doesn't exist.");

        // Create the destination folder if it doesn't exist
        if (!Directory.Exists(destFolder))
            Directory.CreateDirectory(destFolder);

        // For each file in the source folder, copy it to the destination folder
        string[] files = Directory.GetFiles(sourceFolder);
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            string dest = Path.Combine(destFolder, name);

            bytesCopied += (uint)new FileInfo(file).Length;

            // Copy the file
            File.Copy(file, dest);
        }

        // For each folder in the source folder, copy it to the destination folder
        string[] folders = Directory.GetDirectories(sourceFolder);
        foreach (string folder in folders)
        {
            string name = Path.GetFileName(folder);
            string dest = Path.Combine(destFolder, name);

            // Recursively copy the folder
            bytesCopied += CopyFolder(folder, dest);
        }

        return bytesCopied;
    }
}
