using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services;

public class UbiArtEngineDetector : IUbiArtEngineDetector
{
    public UbiArtVersionProfile Detect(string inputPath)
    {
        bool hasCooked = Directory.Exists(Path.Combine(inputPath, "cache", "itf_cooked"));

        // If cooked, look inside cooked cache for world/jd5 or world/jd2015 markers
        if (hasCooked)
        {
            // Search for jd5 or jd2015 anywhere inside cache/itf_cooked
            string cookedRoot = Path.Combine(inputPath, "cache", "itf_cooked");
            // Search nested directories for world/jd5 or world/jd2015
            var cookedDirs = Directory.GetDirectories(cookedRoot, "*", SearchOption.AllDirectories);
            // Check latest to oldest, as newer versions may have both jd2015 and jd5 folders
            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/maps")))
            {
                var p = new UbiArtVersionProfile(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new JsonUbiArtSerializer(), new DefaultUbiArtDataMapper());
                TryPeekSongDescForJDVersion(inputPath, p);
                return p;
            }

            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/jd2015")))
                return new UbiArtVersionProfile(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2015, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer(), new JD2015DataMapper());
            if (cookedDirs.Any(d => d.Replace(Path.DirectorySeparatorChar, '/').Contains("/world/jd5")))
                return new UbiArtVersionProfile(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.JD2014, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer(), new JD2014DataMapper());
        }

        // Uncooked detection: check latest to oldest, as newer versions may have both jd2015 and jd5 folders
        if (Directory.Exists(Path.Combine(inputPath, "world", "maps")))
        {
            var profile = new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            // Try to peek into songdesc to extract JDVersion numeric if present
            TryPeekSongDescForJDVersion(inputPath, profile);
            return profile;
        }

        if (Directory.Exists(Path.Combine(inputPath, "world", "maps", "jd2015")))
            return new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.JD2015, new UbiArtLayoutResolver(), new LuaUbiArtSerializer(), new JD2015DataMapper());
        if (Directory.Exists(Path.Combine(inputPath, "world", "maps", "jd5")))
            return new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.JD2014, new UbiArtLayoutResolver(), new LuaUbiArtSerializer(), new JD2014DataMapper());

        // Flat/uncooked markers: Audio/, Cinematics/, or *.tpl at root
        if (Directory.Exists(Path.Combine(inputPath, "Audio")) ||
            Directory.Exists(Path.Combine(inputPath, "Cinematics")) ||
            Directory.GetFiles(inputPath, "*.tpl").Length != 0)
        {
            var profile = new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            TryPeekSongDescForJDVersion(inputPath, profile);
            return profile;
        }

        // If no cooked marker and no other markers, assume cooked-modern as fallback
        if (!hasCooked)
        {
            var profile = new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            TryPeekSongDescForJDVersion(inputPath, profile);
            return profile;
        }

        var fallbackProfile = new UbiArtVersionProfile(UbiArtContainerStyle.Cooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        TryPeekSongDescForJDVersion(inputPath, fallbackProfile);
        return fallbackProfile;
    }

    private static void TryPeekSongDescForJDVersion(string inputPath, UbiArtVersionProfile profile)
    {
        try
        {
            var candidates = Directory.GetFiles(inputPath, "songdesc.tpl*", SearchOption.AllDirectories);
            if (candidates.Length == 0)
                return;

            string songDescFile = candidates[0];
            string content = File.ReadAllText(songDescFile).TrimEnd('\0');

            // Try JSON first
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(content);
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