using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;

using System.Buffers.Binary;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import;

public class UbiArtEngineDetector(IUbiArtFileSystem? io = null) : IUbiArtEngineDetector
{
    private readonly IUbiArtFileSystem _io = io ?? new PhysicalUbiArtFileSystem();

    public UbiArtVersionProfile Detect(string inputPath)
    {
        // If the input is an IPK file, use the generic UbiArt IPK filesystem and treat paths as relative.
        if (Path.GetExtension(inputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            using UbiArtIpkFileSystem ipk = new(inputPath);
            return DetectWithFileSystem(string.Empty, ipk, inputPath);
        }

        // Default behavior for directories
        return DetectWithFileSystem(inputPath, _io, inputPath);
    }

    private UbiArtVersionProfile DetectWithFileSystem(string basePath, IUbiArtFileSystem fs, string? sourcePath = null)
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
                UbiArtVersionProfile p = CreateModernCookedProfile(detectedPlatform, basePath, fs, sourcePath);
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
        UbiArtVersionProfile fallbackProfile = CreateModernCookedProfile(detectedPlatform, basePath, fs, sourcePath);
        TryPeekSongDescForJDVersion(basePath, fallbackProfile, fs);
        return fallbackProfile;
    }

    private UbiArtVersionProfile CreateModernCookedProfile(UbiArtPlatform platform, string basePath, IUbiArtFileSystem fs, string? sourcePath)
    {
        UbiArtEngineVersion engineVersion = TryDetectLegacySongDescEngineVersion(platform, basePath, fs, sourcePath, out UbiArtEngineVersion legacyVersion)
            ? legacyVersion
            : UbiArtEngineVersion.JD2022;

        IUbiArtSerializer serializer = ShouldUseLegacyBinarySerializer(platform, engineVersion) || ShouldUseBinaryModernSerializer(platform, basePath, fs)
            ? new BinaryUbiArtSerializer()
            : new JsonUbiArtSerializer();

        return new UbiArtVersionProfile(platform, engineVersion, new UbiArtLayoutResolver(), serializer, new DefaultUbiArtDataMapper());
    }

    private static bool ShouldUseLegacyBinarySerializer(UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
    {
        return platform is (UbiArtPlatform.Wii or UbiArtPlatform.X360)
            && engineVersion is >= UbiArtEngineVersion.JD2016 and <= UbiArtEngineVersion.JD2020;
    }

    private bool ShouldUseBinaryModernSerializer(UbiArtPlatform platform, string basePath, IUbiArtFileSystem fs)
    {
        if (platform != UbiArtPlatform.X360)
            return false;

        try
        {
            string[] candidates = [.. GetFilesRecursive(basePath, "*.dtape*", fs)];
            if (candidates.Length == 0)
                candidates = [.. GetFilesRecursive(basePath, "*.tape*", fs)];

            foreach (string candidate in candidates)
            {
                byte[] bytes = fs.ReadAllBytes(candidate);
                byte firstMeaningfulByte = bytes.FirstOrDefault(b => !char.IsWhiteSpace((char)b) && b != 0);
                if (firstMeaningfulByte == (byte)'{' || firstMeaningfulByte == (byte)'[')
                    return false;

                if (firstMeaningfulByte != 0)
                    return true;
            }
        }
        catch
        {
            // Keep generated JSON-style cooked folders working if probing fails.
        }

        return false;
    }

    private bool TryDetectLegacySongDescEngineVersion(UbiArtPlatform platform, string basePath, IUbiArtFileSystem fs, string? sourcePath, out UbiArtEngineVersion engineVersion)
    {
        engineVersion = UbiArtEngineVersion.Unknown;

        if (platform is not (UbiArtPlatform.Wii or UbiArtPlatform.X360))
            return false;

        foreach (byte[] bytes in ReadLegacySongDescCandidates(platform, basePath, fs, sourcePath))
        {
            if (TryReadLegacySongDescEngineVersion(bytes, out engineVersion))
                return true;
        }

        return false;
    }

    private IEnumerable<byte[]> ReadLegacySongDescCandidates(UbiArtPlatform platform, string basePath, IUbiArtFileSystem fs, string? sourcePath)
    {
        const string legacyPattern = "songdesc.main_legacy.tpl*";

        foreach (string candidate in GetFilesRecursive(basePath, legacyPattern, fs))
        {
            byte[]? bytes = TryReadAllBytes(fs, candidate);
            if (bytes != null)
                yield return bytes;
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
            yield break;

        string? parent = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            yield break;

        foreach (string siblingDirectory in Directory.GetDirectories(parent))
        {
            if (string.Equals(siblingDirectory, sourcePath, StringComparison.OrdinalIgnoreCase))
                continue;

            string folderName = Path.GetFileName(siblingDirectory);
            if (!LooksLikeSharedCookedBundle(folderName, platform))
                continue;

            foreach (string candidate in GetFilesRecursive(siblingDirectory, legacyPattern, _io))
            {
                byte[]? bytes = TryReadAllBytes(_io, candidate);
                if (bytes != null)
                    yield return bytes;
            }
        }

        foreach (string siblingIpk in Directory.GetFiles(parent, "*.ipk"))
        {
            if (string.Equals(siblingIpk, sourcePath, StringComparison.OrdinalIgnoreCase))
                continue;

            string ipkName = Path.GetFileNameWithoutExtension(siblingIpk);
            if (!LooksLikeSharedCookedBundle(ipkName, platform))
                continue;

            using UbiArtIpkFileSystem ipk = new(siblingIpk);
            foreach (string candidate in GetFilesRecursive(string.Empty, legacyPattern, ipk))
            {
                byte[]? bytes = TryReadAllBytes(ipk, candidate);
                if (bytes != null)
                    yield return bytes;
            }
        }
    }

    private static bool LooksLikeSharedCookedBundle(string name, UbiArtPlatform platform)
    {
        return name.StartsWith("bundle", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("patch", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? TryReadAllBytes(IUbiArtFileSystem fs, string path)
    {
        try
        {
            return fs.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadLegacySongDescEngineVersion(byte[] bytes, out UbiArtEngineVersion engineVersion)
    {
        engineVersion = UbiArtEngineVersion.Unknown;

        try
        {
            int offset = 0;
            uint version = ReadUInt32BigEndian(bytes, ref offset);
            _ = ReadUInt32BigEndian(bytes, ref offset); // serialized size
            uint baseTypeId = ReadUInt32BigEndian(bytes, ref offset);
            _ = ReadUInt32BigEndian(bytes, ref offset); // base type size

            if (version != 1 || baseTypeId != 0x1B857BCE)
                return false;

            offset += 28; // reserved resource header bytes
            _ = ReadUInt32BigEndian(bytes, ref offset); // component count
            uint componentTypeId = ReadUInt32BigEndian(bytes, ref offset);
            _ = ReadUInt32BigEndian(bytes, ref offset); // component size

            if (componentTypeId != 0x8AC2B5C6)
                return false;

            if (!TrySkipUbiArtString(bytes, ref offset))
                return false;

            uint rawEngineVersion = ReadUInt32BigEndian(bytes, ref offset);
            return TryMapRawEngineVersion(rawEngineVersion, out engineVersion);
        }
        catch
        {
            return false;
        }
    }

    private static uint ReadUInt32BigEndian(byte[] bytes, ref int offset)
    {
        if (offset + sizeof(uint) > bytes.Length)
            throw new EndOfStreamException();

        uint value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
        offset += sizeof(uint);
        return value;
    }

    private static bool TrySkipUbiArtString(byte[] bytes, ref int offset)
    {
        if (offset + sizeof(int) > bytes.Length)
            return false;

        int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);

        if (length < 0 || offset + length > bytes.Length)
            return false;

        offset += length;
        return true;
    }

    private static bool TryMapRawEngineVersion(uint rawEngineVersion, out UbiArtEngineVersion engineVersion)
    {
        engineVersion = UbiArtEngineVersion.Unknown;

        if (!Enum.IsDefined(typeof(UbiArtEngineVersion), (int)rawEngineVersion))
            return false;

        engineVersion = (UbiArtEngineVersion)rawEngineVersion;
        return engineVersion != UbiArtEngineVersion.Unknown;
    }

    private static string NormalizeForTraversal(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        string p = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        p = p.TrimEnd(Path.DirectorySeparatorChar);
        return p;
    }

    private IEnumerable<string> GetDirectoriesRecursive(string root, IUbiArtFileSystem fs)
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

    private IEnumerable<string> GetFilesRecursive(string root, string searchPattern, IUbiArtFileSystem fs)
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

    private void TryPeekSongDescForJDVersion(string inputPath, UbiArtVersionProfile profile, IUbiArtFileSystem fs)
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
                if (fs is PhysicalUbiArtFileSystem)
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
                    if (TryGetJsonEngineVersion(comp, out UbiArtEngineVersion detectedVersion))
                    {
                        profile.EngineVersion = detectedVersion;
                    }
                }

                return;
            }
            catch { }

            // Try LUA deserialization
            try
            {
                SongDesc songDesc = Serialization.LuaTableSerializer.Deserialize<SongDesc>(content);
                if (songDesc?.Components?.Length > 0)
                {
                    InfoComponent component = songDesc.Components[0];
                    if (TryMapRawEngineVersion(component.OriginalJDVersion, out UbiArtEngineVersion detectedVersion) ||
                        TryMapRawEngineVersion(component.JDVersion, out detectedVersion))
                    {
                        profile.EngineVersion = detectedVersion;
                    }
                }
            }
            catch { }
        }
        catch { }
    }

    private static bool TryGetJsonEngineVersion(JsonElement component, out UbiArtEngineVersion engineVersion)
    {
        engineVersion = UbiArtEngineVersion.Unknown;

        if (component.TryGetProperty("OriginalJDVersion", out JsonElement originalVersion) &&
            originalVersion.TryGetUInt32(out uint originalRaw) &&
            TryMapRawEngineVersion(originalRaw, out engineVersion))
        {
            return true;
        }

        if (component.TryGetProperty("JDVersion", out JsonElement jdVersion) &&
            jdVersion.TryGetUInt32(out uint jdRaw) &&
            TryMapRawEngineVersion(jdRaw, out engineVersion))
        {
            return true;
        }

        return false;
    }
}
