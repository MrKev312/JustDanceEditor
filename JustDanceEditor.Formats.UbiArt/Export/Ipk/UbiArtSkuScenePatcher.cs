using JustDanceEditor.Formats.UbiArt.Serialization;

using System.IO.Hashing;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace JustDanceEditor.Formats.UbiArt.Export.Ipk;

internal sealed record UbiArtSkuScenePatchContext(
    string MapName,
    string MapNameLower,
    string SongDescPath,
    string CoverGenericPath,
    string? CoverOnlinePath)
{
    public static UbiArtSkuScenePatchContext FromStaging(string stagingFolder, string platformFolder, string mapName, string mapNameLower)
    {
        List<string> stagedPaths = Directory.Exists(stagingFolder)
            ? [.. Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories)
                .Select(file => UbiArtIpkArchiveIndex.NormalizePath(Path.GetRelativePath(stagingFolder, file)))]
            : [];

        string? songDescPath = stagedPaths
            .Where(path => path.EndsWith($"/{mapNameLower}/songdesc.main_legacy.tpl.ckd", StringComparison.OrdinalIgnoreCase))
            .Select(path => ToResourcePath(path, platformFolder))
            .FirstOrDefault();

        songDescPath ??= stagedPaths
            .Where(path => path.EndsWith($"/{mapNameLower}/songdesc.tpl.ckd", StringComparison.OrdinalIgnoreCase))
            .Select(path => ToResourcePath(path, platformFolder))
            .FirstOrDefault();

        songDescPath ??= $"world/maps/{mapNameLower}/songdesc.tpl";

        string? coverGenericPath = stagedPaths
            .Where(path => path.EndsWith($"/{mapNameLower}_cover_generic.act.ckd", StringComparison.OrdinalIgnoreCase))
            .Select(path => ToResourcePath(path, platformFolder))
            .FirstOrDefault();

        coverGenericPath ??= stagedPaths
            .Where(path => path.EndsWith($"/{mapNameLower}_cover_albumcoach.act.ckd", StringComparison.OrdinalIgnoreCase))
            .Select(path => ToResourcePath(path, platformFolder))
            .FirstOrDefault();

        coverGenericPath ??= $"{GetFolder(songDescPath)}menuart/actors/{mapNameLower}_cover_generic.act";

        string? coverOnlinePath = stagedPaths
            .Where(path => path.EndsWith($"/{mapNameLower}_cover_online.act.ckd", StringComparison.OrdinalIgnoreCase))
            .Select(path => ToResourcePath(path, platformFolder))
            .FirstOrDefault();

        return new UbiArtSkuScenePatchContext(mapName, mapNameLower, songDescPath, coverGenericPath, coverOnlinePath);
    }

    private static string ToResourcePath(string relativePath, string platformFolder)
    {
        string normalized = UbiArtIpkArchiveIndex.NormalizePath(relativePath);
        string cookedPrefix = $"cache/itf_cooked/{platformFolder}/";
        if (normalized.StartsWith(cookedPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[cookedPrefix.Length..];

        if (normalized.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];

        return normalized;
    }

    private static string GetFolder(string resourcePath)
    {
        string normalized = UbiArtIpkArchiveIndex.NormalizePath(resourcePath);
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..(slash + 1)];
    }
}

internal static class UbiArtSkuScenePatcher
{
    public static bool TryPatch(byte[] originalBytes, UbiArtSkuScenePatchContext context, out byte[] updatedBytes)
    {
        updatedBytes = originalBytes;
        if (originalBytes.Length == 0)
            return false;

        byte first = originalBytes.FirstOrDefault(value => value != 0 && !char.IsWhiteSpace((char)value));
        return first == (byte)'<'
            ? TryPatchXml(originalBytes, context, out updatedBytes)
            : TryPatchLegacyBinary(originalBytes, context, out updatedBytes);
    }

    private static bool TryPatchXml(byte[] originalBytes, UbiArtSkuScenePatchContext context, out byte[] updatedBytes)
    {
        updatedBytes = originalBytes;

        string text = Encoding.UTF8.GetString(originalBytes).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(text))
            return false;

        XDocument document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        XElement? scene = document.Descendants("Scene").FirstOrDefault();
        if (scene is null)
            return false;

        bool changed = AddXmlSongActor(scene, context);
        changed |= AddXmlCoverflowEntries(document, context);
        if (!changed)
            return false;

        using MemoryStream stream = new();
        XmlWriterSettings settings = new()
        {
            Encoding = Encoding.Latin1,
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineChars = Environment.NewLine
        };

        using (XmlWriter writer = XmlWriter.Create(stream, settings))
            document.Save(writer);

        updatedBytes = WithNullTerminator(stream.ToArray());
        return true;
    }

    private static bool AddXmlSongActor(XElement scene, UbiArtSkuScenePatchContext context)
    {
        bool alreadyRegistered = scene
            .Descendants("Actor")
            .Any(actor =>
                string.Equals((string?)actor.Attribute("LUA"), context.SongDescPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)actor.Attribute("USERFRIENDLY"), context.MapName, StringComparison.OrdinalIgnoreCase));

        if (alreadyRegistered)
            return false;

        XElement? templateActor = scene
            .Descendants("Actor")
            .FirstOrDefault(actor => ((string?)actor.Attribute("LUA"))?.Contains("/songdesc.tpl", StringComparison.OrdinalIgnoreCase) == true);

        XElement actor = templateActor is not null
            ? new XElement(templateActor)
            : CreateFallbackXmlSongActor();

        actor.SetAttributeValue("USERFRIENDLY", context.MapName);
        actor.SetAttributeValue("LUA", context.SongDescPath);

        scene.Add(Environment.NewLine, "\t\t", new XElement("ACTORS", new XAttribute("NAME", "Actor"), actor), Environment.NewLine, "\t");
        return true;
    }

    private static bool AddXmlCoverflowEntries(XDocument document, UbiArtSkuScenePatchContext context)
    {
        XElement? songDatabaseConfig = document.Descendants("JD_SongDatabaseSceneConfig").FirstOrDefault();
        if (songDatabaseConfig is null)
            return false;

        bool exists = songDatabaseConfig
            .Descendants("CoverflowSong")
            .Any(song => string.Equals((string?)song.Attribute("name"), context.MapName, StringComparison.OrdinalIgnoreCase));

        if (exists)
            return false;

        songDatabaseConfig.Add(CreateCoverflowElement(context.MapName, context.CoverGenericPath));
        if (!string.IsNullOrWhiteSpace(context.CoverOnlinePath) &&
            !string.Equals(context.CoverOnlinePath, context.CoverGenericPath, StringComparison.OrdinalIgnoreCase))
        {
            songDatabaseConfig.Add(CreateCoverflowElement(context.MapName, context.CoverOnlinePath));
        }

        return true;
    }

    private static XElement CreateCoverflowElement(string mapName, string coverPath)
        => new("CoverflowSkuSongs", new XElement("CoverflowSong", new XAttribute("name", mapName), new XAttribute("cover_path", coverPath)));

    private static XElement CreateFallbackXmlSongActor()
        => new(
            "Actor",
            new XAttribute("RELATIVEZ", "0.000000"),
            new XAttribute("SCALE", "1.000000 1.000000"),
            new XAttribute("xFLIPPED", "0"),
            new XAttribute("USERFRIENDLY", ""),
            new XAttribute("MARKER", ""),
            new XAttribute("DEFAULTENABLE", "1"),
            new XAttribute("POS2D", "0.000000 0.000000"),
            new XAttribute("ANGLE", "0.000000"),
            new XAttribute("INSTANCEDATAFILE", ""),
            new XAttribute("LUA", ""),
            new XElement(
                "COMPONENTS",
                new XAttribute("NAME", "JD_SongDescComponent"),
                new XElement("JD_SongDescComponent")));

    private static bool TryPatchLegacyBinary(byte[] originalBytes, UbiArtSkuScenePatchContext context, out byte[] updatedBytes)
    {
        updatedBytes = originalBytes;
        if (originalBytes.Length < 24)
            return false;

        int actorCount = originalBytes[23];
        if (actorCount >= byte.MaxValue)
            return false;

        if (!TryReadLegacyActors(originalBytes, actorCount, out List<LegacySkuActor> actors, out int actorsEnd))
            return false;

        bool actorsChanged = TryRepairDuplicateLegacySongDescResourceIds(originalBytes, actors, out byte[] actorPatchedBytes);
        byte[] actorSourceBytes = actorsChanged ? actorPatchedBytes : originalBytes;

        bool alreadyRegistered = actors.Any(actor =>
            string.Equals(actor.Name, context.MapName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actor.FullPath, context.SongDescPath, StringComparison.OrdinalIgnoreCase));

        byte[] footer = originalBytes[actorsEnd..];
        bool footerChanged = TryPatchLegacyCoverflowFooter(footer, context, out byte[] patchedFooter);
        if (alreadyRegistered)
        {
            if (!actorsChanged && !footerChanged)
                return false;

            byte[] repaired = new byte[actorsEnd + patchedFooter.Length];
            Buffer.BlockCopy(actorSourceBytes, 0, repaired, 0, actorsEnd);
            Buffer.BlockCopy(patchedFooter, 0, repaired, actorsEnd, patchedFooter.Length);
            updatedBytes = repaired;
            return true;
        }

        bool folderFirst = actors
            .Where(actor => actor.IsSongDescActor)
            .Select(actor => actor.FolderFirstPath)
            .Cast<bool?>()
            .FirstOrDefault() ?? false;

        byte[] actorBytes = BuildLegacySongDescActor(context, folderFirst);

        int newLength = originalBytes.Length + actorBytes.Length + (footerChanged ? patchedFooter.Length - footer.Length : 0);
        byte[] result = new byte[newLength];
        Buffer.BlockCopy(actorSourceBytes, 0, result, 0, actorsEnd);
        result[23] = checked((byte)(actorCount + 1));
        Buffer.BlockCopy(actorBytes, 0, result, actorsEnd, actorBytes.Length);

        byte[] footerToCopy = footerChanged ? patchedFooter : footer;
        Buffer.BlockCopy(footerToCopy, 0, result, actorsEnd + actorBytes.Length, footerToCopy.Length);

        updatedBytes = result;
        return true;
    }

    private static bool TryReadLegacyActors(byte[] bytes, int actorCount, out List<LegacySkuActor> actors, out int actorsEnd)
    {
        actors = [];
        actorsEnd = 24;

        try
        {
            for (int actorIndex = 0; actorIndex < actorCount; actorIndex++)
            {
                int offset = actorsEnd;
                SkipUInt32BigEndian(bytes, ref offset);
                offset += 4 * 4;
                string name = ReadString(bytes, ref offset);
                offset += 4 * 8;
                string firstPathPart = ReadString(bytes, ref offset);
                string secondPathPart = ReadString(bytes, ref offset);
                int resourceIdOffset = offset;
                uint resourceId = ReadUInt32BigEndian(bytes, ref offset);
                offset += 4 * 4;

                actors.Add(new LegacySkuActor(name, firstPathPart, secondPathPart, resourceId, resourceIdOffset));
                actorsEnd = offset;
            }

            return actorsEnd <= bytes.Length;
        }
        catch
        {
            actors = [];
            actorsEnd = 24;
            return false;
        }
    }

    private static bool TryRepairDuplicateLegacySongDescResourceIds(
        byte[] originalBytes,
        IReadOnlyCollection<LegacySkuActor> actors,
        out byte[] updatedBytes)
    {
        updatedBytes = originalBytes;

        List<LegacySkuActor> duplicateSongDescActors = actors
            .Where(actor => actor.IsSongDescActor)
            .GroupBy(actor => actor.ResourceId)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .Where(actor => actor.ResourceId == ComputeLegacyResourceId(GetFileName(actor.FullPath)))
            .ToList();

        if (duplicateSongDescActors.Count == 0)
            return false;

        updatedBytes = (byte[])originalBytes.Clone();
        foreach (LegacySkuActor actor in duplicateSongDescActors)
            WriteInt32BigEndian(updatedBytes, actor.ResourceIdOffset, unchecked((int)ComputeLegacyResourceId(actor.FullPath)));

        return true;
    }

    private static byte[] BuildLegacySongDescActor(UbiArtSkuScenePatchContext context, bool folderFirstPath)
    {
        string fileName = GetFileName(context.SongDescPath);
        string folder = GetFolder(context.SongDescPath);

        using MemoryStream stream = new();
        using BigEndianBinaryWriter writer = new(stream);

        writer.Write(0x97CA628Bu);
        writer.Write(0);
        writer.Write(1.0f);
        writer.Write(1.0f);
        writer.Write(0);
        writer.WriteUbiArtString(context.MapName);
        writer.Write(uint.MaxValue);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(uint.MaxValue);
        writer.Write(0);

        if (folderFirstPath)
        {
            writer.WriteUbiArtString(folder);
            writer.WriteUbiArtString(fileName);
        }
        else
        {
            writer.WriteUbiArtString(fileName);
            writer.WriteUbiArtString(folder);
        }

        writer.Write(ComputeLegacyResourceId(context.SongDescPath));
        writer.Write(2);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0xE07FCC3Fu);

        return stream.ToArray();
    }

    private static bool TryPatchLegacyCoverflowFooter(byte[] footer, UbiArtSkuScenePatchContext context, out byte[] patchedFooter)
    {
        patchedFooter = footer;
        if (footer.Length < 32)
            return false;

        foreach (int headerOffset in new[] { 16, 0 })
        {
            foreach (int headerStringCount in new[] { 3, 4 })
            {
                if (TryPatchLegacyCoverflowFooter(footer, context, headerOffset, headerStringCount, out patchedFooter))
                    return !ReferenceEquals(patchedFooter, footer);
            }
        }

        return false;
    }

    private static bool TryPatchLegacyCoverflowFooter(
        byte[] footer,
        UbiArtSkuScenePatchContext context,
        int headerOffset,
        int headerStringCount,
        out byte[] patchedFooter)
    {
        patchedFooter = footer;
        try
        {
            int offset = headerOffset;
            for (int index = 0; index < headerStringCount; index++)
                SkipString(footer, ref offset);

            offset += 8;

            int countOffset = offset;
            int coverCount = ReadInt32BigEndian(footer, ref offset);
            if (coverCount < 0 || coverCount > 10000)
                return false;

            bool? folderFirst = null;
            for (int index = 0; index < coverCount; index++)
            {
                string existingName = ReadString(footer, ref offset);
                string firstPathPart = ReadString(footer, ref offset);
                string secondPathPart = ReadString(footer, ref offset);
                offset += 4;
                offset += 8;

                folderFirst ??= LooksLikeFolder(firstPathPart) && !LooksLikeFolder(secondPathPart);

                if (string.Equals(existingName, context.MapName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            int tailLength = footer.Length - offset;
            if (tailLength != 0 && (tailLength != 4 || !IsZeroPadding(footer, offset, tailLength)))
                return false;

            byte[] entry = BuildLegacyCoverflowEntry(context, folderFirst ?? true);
            byte[] patched = new byte[footer.Length + entry.Length];
            Buffer.BlockCopy(footer, 0, patched, 0, offset);
            WriteInt32BigEndian(patched, countOffset, coverCount + 1);
            Buffer.BlockCopy(entry, 0, patched, offset, entry.Length);
            if (tailLength > 0)
                Buffer.BlockCopy(footer, offset, patched, offset + entry.Length, tailLength);
            patchedFooter = patched;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BuildLegacyCoverflowEntry(UbiArtSkuScenePatchContext context, bool folderFirstPath)
    {
        string fileName = GetFileName(context.CoverGenericPath);
        string folder = GetFolder(context.CoverGenericPath);

        using MemoryStream stream = new();
        using BigEndianBinaryWriter writer = new(stream);
        writer.WriteUbiArtString(context.MapName);

        if (folderFirstPath)
        {
            writer.WriteUbiArtString(folder);
            writer.WriteUbiArtString(fileName);
        }
        else
        {
            writer.WriteUbiArtString(fileName);
            writer.WriteUbiArtString(folder);
        }

        writer.Write(Crc32.HashToUInt32(Encoding.UTF8.GetBytes(fileName)));
        writer.Write(0);
        writer.Write(0);
        return stream.ToArray();
    }

    private static byte[] WithNullTerminator(byte[] bytes)
    {
        if (bytes.Length > 0 && bytes[^1] == 0)
            return bytes;

        byte[] result = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    private static string ReadString(byte[] bytes, ref int offset)
    {
        int length = ReadInt32BigEndian(bytes, ref offset);
        if (length < 0 || offset + length > bytes.Length)
            throw new InvalidDataException("Invalid UbiArt string length.");

        string value = Encoding.UTF8.GetString(bytes, offset, length).TrimEnd('\0');
        offset += length;
        return value;
    }

    private static void SkipString(byte[] bytes, ref int offset)
    {
        int length = ReadInt32BigEndian(bytes, ref offset);
        if (length < 0 || offset + length > bytes.Length)
            throw new InvalidDataException("Invalid UbiArt string length.");

        offset += length;
    }

    private static int ReadInt32BigEndian(byte[] bytes, ref int offset)
    {
        uint value = ReadUInt32BigEndian(bytes, ref offset);
        return unchecked((int)value);
    }

    private static uint ReadUInt32BigEndian(byte[] bytes, ref int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            throw new InvalidDataException("Unexpected end of binary skuscene.");

        uint value =
            ((uint)bytes[offset] << 24) |
            ((uint)bytes[offset + 1] << 16) |
            ((uint)bytes[offset + 2] << 8) |
            bytes[offset + 3];
        offset += 4;
        return value;
    }

    private static void SkipUInt32BigEndian(byte[] bytes, ref int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            throw new InvalidDataException("Unexpected end of binary skuscene.");

        offset += 4;
    }

    private static void WriteInt32BigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = unchecked((byte)(value >> 24));
        bytes[offset + 1] = unchecked((byte)(value >> 16));
        bytes[offset + 2] = unchecked((byte)(value >> 8));
        bytes[offset + 3] = unchecked((byte)value);
    }

    private static uint ComputeLegacyResourceId(string resourcePath)
        => Crc32.HashToUInt32(Encoding.UTF8.GetBytes(UbiArtIpkArchiveIndex.NormalizePath(resourcePath)));

    private static bool IsZeroPadding(byte[] bytes, int offset, int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (bytes[offset + index] != 0)
                return false;
        }

        return true;
    }

    private static string GetFileName(string path)
    {
        string normalized = UbiArtIpkArchiveIndex.NormalizePath(path);
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static string GetFolder(string path)
    {
        string normalized = UbiArtIpkArchiveIndex.NormalizePath(path);
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..(slash + 1)];
    }

    private sealed record LegacySkuActor(
        string Name,
        string FirstPathPart,
        string SecondPathPart,
        uint ResourceId,
        int ResourceIdOffset)
    {
        public bool IsSongDescActor =>
            FirstPathPart.Contains("songdesc", StringComparison.OrdinalIgnoreCase) ||
            SecondPathPart.Contains("songdesc", StringComparison.OrdinalIgnoreCase);

        public bool FolderFirstPath => LooksLikeFolder(FirstPathPart) && !LooksLikeFolder(SecondPathPart);

        public string FullPath => FolderFirstPath
            ? FirstPathPart + SecondPathPart
            : SecondPathPart + FirstPathPart;

        private static bool LooksLikeFolder(string value)
            => value.EndsWith("/", StringComparison.Ordinal) || !Path.HasExtension(value.Replace("/", Path.DirectorySeparatorChar.ToString()));
    }

    private static bool LooksLikeFolder(string value)
        => value.EndsWith("/", StringComparison.Ordinal) || !Path.HasExtension(value.Replace("/", Path.DirectorySeparatorChar.ToString()));
}