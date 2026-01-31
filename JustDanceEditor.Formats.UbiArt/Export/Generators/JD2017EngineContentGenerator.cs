using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Import;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

public class JD2017EngineContentGenerator(UbiArtEngineVersion EngineVersion) : ModernEngineContentGenerator(EngineVersion)
{
    public override byte[] GenerateMenuArtActor(string textureName, string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write([0, 0, 0, 1, 0, 0, 0, 0]);
        WriteBigEndianFloat(writer, 1.0f);
        WriteBigEndianFloat(writer, 1.0f);
        // Old engine uses 28 empty bytes here instead of 12, then a 1, then 16
        writer.Write(new byte[28]);
        writer.Write((uint)0);
        writer.Write(0xFFFFFFFF);
        writer.Write((uint)0);

        WriteString(writer, "tpl_materialgraphiccomponent2d.tpl");
        WriteString(writer, "enginedata/actortemplates/");

        writer.Write([0xB4, 0xA8, 0x17, 0xA8]);
        writer.Write(new byte[8]);
        WriteBigEndian32(writer, 1);
        writer.Write([0x72, 0xB6, 0x1F, 0xC5]);

        WriteBigEndianFloat(writer, 1.0f);
        WriteBigEndianFloat(writer, 1.0f);
        WriteBigEndianFloat(writer, 1.0f);
        WriteBigEndianFloat(writer, 1.0f);

        writer.Write(new byte[16]);
        writer.Write(0xFFFFFFFF);
        writer.Write((uint)0);
        writer.Write([0, 0, 0]);
        writer.Write(textureName.EndsWith("_map_bkg") ? (byte)0x01 : (byte)0x06);
        writer.Write(new byte[8]);

        WriteString(writer, $"{textureName}.tga");
        WriteString(writer, $"world/maps/{mapNameLower}/menuart/textures/");

        // Texture Hash
        if (textureName.EndsWith("_map_bkg"))
            writer.Write([0x75, 0xB8, 0xD3, 0x38]);
        else
            writer.Write([0xCA, 0x88, 0x8F, 0xC5]);

        writer.Write(new byte[8]);
        for (int i = 0; i < 8; i++)
        {
            writer.Write((uint)0);
            writer.Write(0xFFFFFFFF);
            writer.Write((long)0);
        }

        writer.Write(new byte[8]);
        writer.Write(0xFFFFFFFF);
        writer.Write((uint)0);

        WriteString(writer, "multitexture_1layer.msh");
        WriteString(writer, "world/_common/matshader/");

        writer.Write([0xD7, 0xE7, 0xD9, 0xC7]);
        // Old engine: write 12 bytes here (instead of 40) and skip the first 1.0f write
        writer.Write(new byte[12]);
        // (skip WriteBigEndianFloat(writer, 1.0f);)
        writer.Write(0xFFFFFFFF);
        writer.Write(0xFFFFFFFF);
        writer.Write(new byte[12]);
        WriteBigEndianFloat(writer, 1.0f);
        writer.Write(new byte[11]);
        writer.Write((byte)0x01);

        return ms.ToArray();
    }
}