using JustDanceEditor.Formats.UbiArt.Model;

using System.Buffers.Binary;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import;

internal static class UbiArtSongDescriptorVersionReader
{
    public static bool TryReadModern(string content, out UbiArtEngineVersion engineVersion)
    {
        engineVersion = UbiArtEngineVersion.Unknown;

        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("COMPONENTS", out JsonElement components) ||
                components.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement component in components.EnumerateArray())
            {
                if (TryGetJsonEngineVersion(component, out UbiArtEngineVersion candidate) && candidate > engineVersion)
                    engineVersion = candidate;
            }

            return engineVersion != UbiArtEngineVersion.Unknown;
        }
        catch (JsonException)
        {
        }

        try
        {
            SongDesc songDesc = Serialization.LuaTableSerializer.Deserialize<SongDesc>(content);
            if (songDesc?.Components == null)
                return false;

            foreach (InfoComponent component in songDesc.Components)
            {
                if ((TryMapEngineVersion(component.JDVersion, out UbiArtEngineVersion candidate) ||
                     TryMapEngineVersion(component.OriginalJDVersion, out candidate)) &&
                    candidate > engineVersion)
                {
                    engineVersion = candidate;
                }
            }

            return engineVersion != UbiArtEngineVersion.Unknown;
        }
        catch
        {
            engineVersion = UbiArtEngineVersion.Unknown;
            return false;
        }
    }

    public static bool TryReadLegacy(ReadOnlySpan<byte> bytes, out UbiArtEngineVersion engineVersion)
    {
        engineVersion = UbiArtEngineVersion.Unknown;

        try
        {
            int offset = 0;
            uint version = ReadUInt32BigEndian(bytes, ref offset);
            SkipUInt32BigEndian(bytes, ref offset); // serialized size
            uint baseTypeId = ReadUInt32BigEndian(bytes, ref offset);
            SkipUInt32BigEndian(bytes, ref offset); // base type size

            if (version != 1 || baseTypeId != 0x1B857BCE)
                return false;

            offset += 28; // reserved resource header bytes
            SkipUInt32BigEndian(bytes, ref offset); // component count
            uint componentTypeId = ReadUInt32BigEndian(bytes, ref offset);
            SkipUInt32BigEndian(bytes, ref offset); // component size

            if (componentTypeId != 0x8AC2B5C6 || !TrySkipUbiArtString(bytes, ref offset))
                return false;

            return TryMapEngineVersion(ReadUInt32BigEndian(bytes, ref offset), out engineVersion);
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private static bool TryGetJsonEngineVersion(JsonElement component, out UbiArtEngineVersion engineVersion)
    {
        if (component.TryGetProperty("JDVersion", out JsonElement jdVersion) &&
            jdVersion.TryGetUInt32(out uint jdRaw) &&
            TryMapEngineVersion(jdRaw, out engineVersion))
        {
            return true;
        }

        if (component.TryGetProperty("OriginalJDVersion", out JsonElement originalVersion) &&
            originalVersion.TryGetUInt32(out uint originalRaw) &&
            TryMapEngineVersion(originalRaw, out engineVersion))
        {
            return true;
        }

        engineVersion = UbiArtEngineVersion.Unknown;
        return false;
    }

    private static bool TryMapEngineVersion(uint rawEngineVersion, out UbiArtEngineVersion engineVersion)
    {
        engineVersion = UbiArtEngineVersion.Unknown;
        if (!Enum.IsDefined(typeof(UbiArtEngineVersion), (int)rawEngineVersion))
            return false;

        engineVersion = (UbiArtEngineVersion)rawEngineVersion;
        return engineVersion is >= UbiArtEngineVersion.JD2014 and <= UbiArtEngineVersion.JD2022;
    }

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset + sizeof(uint) > bytes.Length)
            throw new EndOfStreamException();

        uint value = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, sizeof(uint)));
        offset += sizeof(uint);
        return value;
    }

    private static void SkipUInt32BigEndian(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset + sizeof(uint) > bytes.Length)
            throw new EndOfStreamException();

        offset += sizeof(uint);
    }

    private static bool TrySkipUbiArtString(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset + sizeof(int) > bytes.Length)
            return false;

        int length = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        if (length < 0 || length > bytes.Length - offset)
            return false;

        offset += length;
        return true;
    }
}
