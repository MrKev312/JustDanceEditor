using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import;

public class UbiArtEngineDetector(JDI.Services.IFileSystem? io = null) : IUbiArtEngineDetector
{
    private readonly JDI.Services.IFileSystem _io = io ?? new JDI.Services.SystemFileSystem();

    public UbiArtVersionProfile Detect(string inputPath)
    {
        // If the input is an IPK file, use an IpkFileSystem and treat paths as relative (no inputPath prefix)
        if (Path.GetExtension(inputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            using IpkFileSystem ipk = new(inputPath);
            return DetectWithFileSystem(string.Empty, ipk);
        }

        // Default behavior for directories
        return DetectWithFileSystem(inputPath, _io);
    }

    private UbiArtVersionProfile DetectWithFileSystem(string basePath, JDI.Services.IFileSystem fs)
    {
        bool hasCooked = fs.DirectoryExists(fs.Combine(basePath, "cache", "itf_cooked"));

        // Default platform when unknown (legacy behaviour)
        UbiArtPlatform detectedPlatform = UbiArtPlatform.WiiU;

        // If cooked, look inside cooked cache for world/jd5 or world/jd2015 markers
        if (hasCooked)
        {
            // Search for the platform folder under cache/itf_cooked first (expects a single platform folder)
            string cookedRoot = fs.Combine(basePath, "cache", "itf_cooked");
            string[] platformFolders = fs.GetDirectories(cookedRoot);
            if (platformFolders.Length == 0)
                throw new DirectoryNotFoundException("No platform folders found in the itf_cooked folder.");
            if (platformFolders.Length > 1)
                throw new DirectoryNotFoundException("Multiple platform folders found in the itf_cooked folder, this is not supported.");

            string platformFolderName = Path.GetFileName(platformFolders[0]);
            // Parse the platform folder name into the enum via reflection and fail fast on unknown values
            if (!Enum.TryParse(platformFolderName, true, out detectedPlatform))
                throw new InvalidOperationException($"Unknown UbiArt platform folder '{platformFolderName}' in cache/itf_cooked.");

            // 'uncooked' is not a valid cooked platform; treat it as an error
            if (detectedPlatform == UbiArtPlatform.Uncooked)
                throw new InvalidOperationException($"Invalid platform 'uncooked' inside cache/itf_cooked.");

            // Search nested directories for world/jd5 or world/jd2015
            // This uses the cookedRoot as base so checks include the platform folder
            string[] cookedDirs = [.. GetDirectoriesRecursive(cookedRoot, fs)];
            // Check latest to oldest, as newer versions may have both jd2015 and jd5 folders
            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/maps")))
            {
                UbiArtVersionProfile p = new(detectedPlatform, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer(), new DefaultUbiArtDataMapper());
                TryPeekSongDescForJDVersion(basePath, p, fs);
                return p;
            }

            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/jd2015")))
            {
                UbiArtVersionProfile prof = new(detectedPlatform, UbiArtEngineVersion.JD2015, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer(), new JD2015DataMapper());
                TryPeekSongDescForJDVersion(basePath, prof, fs);
                return prof;
            }

            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/jd5")))
            {
                UbiArtVersionProfile prof = new(detectedPlatform, UbiArtEngineVersion.JD2014, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer(), new JD2014DataMapper());
                TryPeekSongDescForJDVersion(basePath, prof, fs);
                return prof;
            }
        }

        // Uncooked detection: check latest to oldest, as newer versions may have both jd2015 and jd5 folders
        if (fs.DirectoryExists(fs.Combine(basePath, "world", "maps", "jd2015")))
        {
            UbiArtVersionProfile prof = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2015, new UbiArtLayoutResolver(), new LuaUbiArtSerializer(), new JD2015DataMapper());
            TryPeekSongDescForJDVersion(basePath, prof, fs);
            return prof;
        }

        if (fs.DirectoryExists(fs.Combine(basePath, "world", "maps", "jd5")))
        {
            UbiArtVersionProfile prof = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2014, new UbiArtLayoutResolver(), new LuaUbiArtSerializer(), new JD2014DataMapper());
            TryPeekSongDescForJDVersion(basePath, prof, fs);
            return prof;
        }

        if (fs.DirectoryExists(fs.Combine(basePath, "world", "maps")))
        {
            UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            // Try to peek into songdesc to extract JDVersion numeric if present
            TryPeekSongDescForJDVersion(basePath, profile, fs);
            return profile;
        }

        // Flat/uncooked markers: Audio/, Cinematics/, or *.tpl at root
        if (fs.DirectoryExists(fs.Combine(basePath, "Audio")) ||
            fs.DirectoryExists(fs.Combine(basePath, "Cinematics")) ||
            fs.GetFiles(basePath, "*.tpl").Length != 0)
        {
            UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            TryPeekSongDescForJDVersion(basePath, profile, fs);
            return profile;
        }

        // If no cooked marker and no other markers, assume uncooked as fallback
        if (!hasCooked)
        {
            UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            TryPeekSongDescForJDVersion(basePath, profile, fs);
            return profile;
        }

        // If we got here, we had a cooked input but no recognizable world maps/jd folders - fall back to detected platform with JSON serializer
        UbiArtVersionProfile fallbackProfile = new(detectedPlatform, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        TryPeekSongDescForJDVersion(basePath, fallbackProfile, fs);
        return fallbackProfile;
    }

    private static string NormalizeForTraversal(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        string p = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        p = p.TrimEnd(Path.DirectorySeparatorChar);
        return p;
    }

    private IEnumerable<string> GetDirectoriesRecursive(string root, JDI.Services.IFileSystem fs)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> stack = new();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            string nCurrent = NormalizeForTraversal(current);
            if (!visited.Add(nCurrent))
                continue;

            foreach (string dir in fs.GetDirectories(current))
            {
                string nDir = NormalizeForTraversal(dir);
                // Skip self-references or malformed entries that would cause cycles
                if (string.IsNullOrEmpty(nDir) || string.Equals(nDir, nCurrent, StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return dir;
                stack.Push(dir);
            }
        }
    }

    private IEnumerable<string> GetFilesRecursive(string root, string searchPattern, JDI.Services.IFileSystem fs)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> stack = new();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            string nCurrent = NormalizeForTraversal(current);
            if (!visited.Add(nCurrent))
                continue;

            foreach (string file in fs.GetFiles(current, searchPattern))
            {
                yield return fs.Combine(current, file);
            }

            foreach (string dir in fs.GetDirectories(current))
            {
                string nDir = NormalizeForTraversal(dir);
                if (string.IsNullOrEmpty(nDir) || string.Equals(nDir, nCurrent, StringComparison.OrdinalIgnoreCase) || visited.Contains(nDir))
                    continue;

                stack.Push(dir);
            }
        }
    }

    private void TryPeekSongDescForJDVersion(string inputPath, UbiArtVersionProfile profile, JDI.Services.IFileSystem fs)
    {
        try
        {
            string[] candidates = [.. GetFilesRecursive(inputPath, "songdesc.tpl*", fs)];
            if (candidates.Length == 0)
                return;

            string songDescFile = candidates[0];

            // Read via a stream so we don't rely on a single text API; trim trailing NULs afterwards
            string content;
            try
            {
                if (fs is JDI.Services.SystemFileSystem)
                {
                    using Stream s = File.OpenRead(songDescFile);
                    using StreamReader sr = new(s, System.Text.Encoding.UTF8);
                    content = sr.ReadToEnd().TrimEnd('\0');
                }
                else
                {
                    // Use the IFileSystem abstraction for testable (mock) file reads
                    content = fs.ReadAllText(songDescFile).TrimEnd('\0');
                }
            }
            catch
            {
                // If we can't open the file via stream, fall back to path-based read
                content = fs.ReadAllText(songDescFile).TrimEnd('\0');
            }

            // Try JSON first
            try
            {
                using JsonDocument doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("COMPONENTS", out JsonElement components) && components.ValueKind == JsonValueKind.Array)
                {
                    JsonElement comp = components[0];
                    if (comp.TryGetProperty("JDVersion", out JsonElement jdVersionProp) && jdVersionProp.TryGetUInt32(out uint jdVersion))
                    {
                        // JDVersion found but not used to override EngineVersion enum
                    }
                }

                return;
            }
            catch { }

            // Try LUA deserialization
            try
            {
                SongDesc songDesc = UbiArt.Serialization.LuaTableSerializer.Deserialize<SongDesc>(content);
                if (songDesc?.Components?.Length > 0)
                {
                    // JDVersion found but not used to override EngineVersion enum
                }
            }
            catch { }
        }
        catch { }
    }
}