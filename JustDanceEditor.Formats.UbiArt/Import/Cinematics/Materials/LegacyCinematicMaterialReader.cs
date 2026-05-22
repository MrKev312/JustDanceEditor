using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal static class LegacyCinematicMaterialReader
{
    private const int MaterialBodyOffset = 0x64;
    private const int MaterialLayerSerializedSize = 0x3C;
    private const int UvModifierSerializedSize = 0x30;
    private const int LayerCount = 4;
    private const int DefaultLayerBlendMode = 2;
    private const int GfxBlendCopy = 1;
    private const int GfxBlendAlpha = 2;
    private const int GfxBlendAlphaPremult = 3;
    private const int GfxBlendAdd = 6;
    private const int GfxBlendAddAlpha = 7;
    private const int GfxBlendMul2X = 17;
    private const int GfxBlendScreen = 21;

    public static LegacyCinematicMaterial Read(byte[] bytes)
    {
        if (bytes.Length < MaterialBodyOffset + 8)
            throw new InvalidDataException("Legacy material is too short.");

        LegacyCinematicBinaryReader reader = new(bytes, MaterialBodyOffset);
        int blendMode = reader.ReadInt32();
        int layerBlendMode = DefaultLayerBlendMode;
        List<LegacyCinematicMaterialLayer> layers = new(LayerCount);

        for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            if (reader.Remaining < 4)
                break;

            int layerSize = reader.ReadInt32();
            if (layerSize != MaterialLayerSerializedSize)
                throw new InvalidDataException($"Invalid legacy material layer marker 0x{layerSize:X8} at 0x{reader.Offset - 4:X}.");

            layers.Add(ReadLayer(reader, layerBlendMode));

            if (layerIndex < LayerCount - 1)
            {
                if (reader.Remaining < 4)
                    break;

                layerBlendMode = reader.ReadInt32();
            }
        }

        if (layers.Count == 0)
            return LegacyCinematicMaterial.Empty;

        if (layers.All(layer => !layer.Enabled))
        {
            LegacyCinematicMaterialLayer layer0 = layers[0];
            return new LegacyCinematicMaterial(
                blendMode,
                [
                    layer0 with
                    {
                        Enabled = true,
                        BlendMode = DefaultLayerBlendMode,
                        TextureUsage = LegacyCinematicTextureUsage.EntireTexture,
                        DiffuseColor = LegacyCinematicMaterialColor.White
                    }
                ]);
        }

        return new LegacyCinematicMaterial(blendMode, layers);
    }

    public static bool TryCreateShaderFallback(string? shaderPath, out LegacyCinematicMaterial material)
    {
        material = LegacyCinematicMaterial.Empty;
        if (string.IsNullOrWhiteSpace(shaderPath) ||
            !Path.GetExtension(shaderPath).Equals(".msh", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string shaderName = Path.GetFileNameWithoutExtension(shaderPath).ToLowerInvariant();
        int blendMode = shaderName switch
        {
            "addalpha" or "separateaddalpha" => GfxBlendAddAlpha,
            "add" => GfxBlendAdd,
            "screen" => GfxBlendScreen,
            "premultalpha" => GfxBlendAlphaPremult,
            "mul2x" => GfxBlendMul2X,
            "alphatest" => GfxBlendCopy,
            _ when shaderName.Contains("addalpha", StringComparison.Ordinal) => GfxBlendAddAlpha,
            _ when shaderName.Contains("add", StringComparison.Ordinal) => GfxBlendAdd,
            _ when shaderName.Contains("screen", StringComparison.Ordinal) => GfxBlendScreen,
            _ when shaderName.Contains("premult", StringComparison.Ordinal) => GfxBlendAlphaPremult,
            _ => GfxBlendAlpha
        };

        material = new LegacyCinematicMaterial(blendMode, [LegacyCinematicMaterialLayer.Default]);
        return true;
    }

    private static LegacyCinematicMaterialLayer ReadLayer(LegacyCinematicBinaryReader reader, int blendMode)
    {
        bool enabled = reader.ReadUInt32() != 0;
        TextureAddressMode addressU = ReadAddressMode(reader.ReadInt32());
        TextureAddressMode addressV = ReadAddressMode(reader.ReadInt32());
        _ = reader.ReadUInt32();
        float serializedBlue = reader.ReadSingle();
        float serializedGreen = reader.ReadSingle();
        float serializedRed = reader.ReadSingle();
        float serializedAlpha = reader.ReadSingle();
        LegacyCinematicMaterialColor diffuseColor = new(
            serializedRed,
            serializedGreen,
            serializedBlue,
            serializedAlpha);
        LegacyCinematicTextureUsage textureUsage = ReadTextureUsage(reader.ReadInt32());

        int uvModifierCount = reader.ReadInt32();
        if (uvModifierCount is < 0 or > 32)
            throw new InvalidDataException($"Invalid legacy UV modifier count {uvModifierCount} at 0x{reader.Offset - 4:X}.");

        List<LegacyCinematicUvModifier> modifiers = new(uvModifierCount);
        for (int index = 0; index < uvModifierCount; index++)
        {
            int modifierSize = reader.ReadInt32();
            if (modifierSize != UvModifierSerializedSize)
                throw new InvalidDataException($"Invalid legacy UV modifier marker 0x{modifierSize:X8} at 0x{reader.Offset - 4:X}.");

            int modifierStart = reader.Offset;
            modifiers.Add(ReadUvModifier(reader));
            int consumed = reader.Offset - modifierStart;
            if (consumed < modifierSize)
                reader.Skip(modifierSize - consumed);
        }

        return new LegacyCinematicMaterialLayer(enabled, addressU, addressV, modifiers, blendMode, textureUsage, diffuseColor);
    }

    private static LegacyCinematicUvModifier ReadUvModifier(LegacyCinematicBinaryReader reader) =>
        new(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadUInt32() != 0,
            reader.ReadUInt32() != 0,
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadUInt32() != 0,
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());

    private static TextureAddressMode ReadAddressMode(int raw) =>
        Enum.IsDefined(typeof(TextureAddressMode), raw)
            ? (TextureAddressMode)raw
            : TextureAddressMode.Wrap;

    private static LegacyCinematicTextureUsage ReadTextureUsage(int raw) =>
        Enum.IsDefined(typeof(LegacyCinematicTextureUsage), raw)
            ? (LegacyCinematicTextureUsage)raw
            : LegacyCinematicTextureUsage.EntireTexture;
}