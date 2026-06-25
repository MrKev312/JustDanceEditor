using System.Security.Cryptography;

namespace JustDanceEditor.Formats.JDI.Utilities;

public static class FileHashing
{
    public static string GetFileMD5(string filePath)
    {
        byte[] hash = MD5.HashData(File.ReadAllBytes(filePath));
        return Convert.ToHexStringLower(hash);
    }
}