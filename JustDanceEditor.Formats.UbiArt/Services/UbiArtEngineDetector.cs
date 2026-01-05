using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services;

public class UbiArtEngineDetector : IUbiArtEngineDetector
{
    private readonly JDI.Services.IFileSystem _io;

    public UbiArtEngineDetector(JDI.Services.IFileSystem? io = null)
    {
        _io = io ?? new JDI.Services.SystemFileSystem();
    }

    public UbiArtVersionProfile Detect(string inputPath)
    {
        bool hasCooked = _io.DirectoryExists(_io.Combine(inputPath, "cache", "itf_cooked"));

        // If cooked, look inside cooked cache for world/jd5 or world/jd2015 markers
        if (hasCooked)
        {
            // Search for jd5 or jd2015 anywhere inside cache/itf_cooked
            string cookedRoot = _io.Combine(inputPath, "cache", "itf_cooked");
            // Search nested directories for world/jd5 or world/jd2015
            var cookedDirs = GetDirectoriesRecursive(cookedRoot).ToArray();
            // Check latest to oldest, as newer versions may have both jd2015 and jd5 folders
            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/maps")))
            {
                UbiArtVersionProfile p = new(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new JsonUbiArtSerializer(), new DefaultUbiArtDataMapper());
                TryPeekSongDescForJDVersion(inputPath, p);
                return p;
            }

            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/jd2015")))
                return new UbiArtVersionProfile(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2015, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer(), new JD2015DataMapper());
            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/jd5")))
                return new UbiArtVersionProfile(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2014, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer(), new JD2014DataMapper());
        }

        // Uncooked detection: check latest to oldest, as newer versions may have both jd2015 and jd5 folders
        if (_io.DirectoryExists(_io.Combine(inputPath, "world", "maps", "jd2015")))
            return new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.JD2015, new UbiArtLayoutResolver(), new LuaUbiArtSerializer(), new JD2015DataMapper());
        if (_io.DirectoryExists(_io.Combine(inputPath, "world", "maps", "jd5")))
            return new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.JD2014, new UbiArtLayoutResolver(), new LuaUbiArtSerializer(), new JD2014DataMapper());

        if (_io.DirectoryExists(_io.Combine(inputPath, "world", "maps")))
        {
            UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            // Try to peek into songdesc to extract JDVersion numeric if present
            TryPeekSongDescForJDVersion(inputPath, profile);
            return profile;
        }

        // Flat/uncooked markers: Audio/, Cinematics/, or *.tpl at root
        if (_io.DirectoryExists(_io.Combine(inputPath, "Audio")) ||
            _io.DirectoryExists(_io.Combine(inputPath, "Cinematics")) ||
            _io.GetFiles(inputPath, "*.tpl").Length != 0)
        {
            UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            TryPeekSongDescForJDVersion(inputPath, profile);
            return profile;
        }

        // If no cooked marker and no other markers, assume cooked-modern as fallback
        if (!hasCooked)
        {
            UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            TryPeekSongDescForJDVersion(inputPath, profile);
            return profile;
        }

        UbiArtVersionProfile fallbackProfile = new(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        TryPeekSongDescForJDVersion(inputPath, fallbackProfile);
        return fallbackProfile;
    }

    private IEnumerable<string> GetDirectoriesRecursive(string root)
    {
        foreach (var dir in _io.GetDirectories(root))
        {
            yield return dir;
            foreach (var child in GetDirectoriesRecursive(dir))
                yield return child;
        }
    }

    private IEnumerable<string> GetFilesRecursive(string root, string searchPattern)
    {
        foreach (var file in _io.GetFiles(root, searchPattern))
            yield return file;

        foreach (var dir in _io.GetDirectories(root))
        {
            foreach (var file in GetFilesRecursive(dir, searchPattern))
                yield return file;
        }
    }

    private void TryPeekSongDescForJDVersion(string inputPath, UbiArtVersionProfile profile)
    {
        try
        {
            var candidates = GetFilesRecursive(inputPath, "songdesc.tpl*").ToArray();
            if (candidates.Length == 0)
                return;

            string songDescFile = candidates[0];
            string content = _io.ReadAllText(songDescFile).TrimEnd('\0');

            // Try JSON first
            try
            {
                using JsonDocument doc = System.Text.Json.JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("COMPONENTS", out JsonElement components) && components.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    JsonElement comp = components[0];
                    if (comp.TryGetProperty("JDVersion", out JsonElement jdVersionProp) && jdVersionProp.TryGetUInt32(out uint jdVersion))
                        profile.EngineNumericVersion = jdVersion;
                    else if (comp.TryGetProperty("OriginalJDVersion", out JsonElement origProp) && origProp.TryGetUInt32(out uint orig))
                        profile.EngineNumericVersion = orig;
                }

                return;
            }
            catch { }

            // Try LUA deserialization
            try
            {
                SongDesc songDesc = JustDanceEditor.Formats.UbiArt.Serialization.LuaTableSerializer.Deserialize<SongDesc>(content);
                if (songDesc?.COMPONENTS?.Length > 0)
                {
                    profile.EngineNumericVersion = songDesc.COMPONENTS[0].JDVersion;
                }
            }
            catch { }
        }
        catch { }
    }
}