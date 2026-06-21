using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

using KevInc.UbiArt.Cinematics.Core;

using System.Globalization;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal static class CinematicMaterialReader
{
    internal const double AnimatedShadowLayerAlpha = 77.0 / 255.0;
    private const int MaterialBodyOffset = 0x64;
    private const int MaterialLayerWithAlphaThresholdSerializedSize = 0x3C;
    private const int MaterialLayerWithoutAlphaThresholdSerializedSize = 0x38;
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

    public static CinematicRenderMaterial Read(byte[] bytes)
    {
        if (IsJsonMaterial(bytes))
            return ReadJson(bytes);

        if (bytes.Length < MaterialBodyOffset + 8)
            throw new InvalidDataException("Legacy material is too short.");

        CinematicBinaryReader reader = new(bytes, MaterialBodyOffset);
        int blendMode = reader.ReadInt32();
        int layerBlendMode = DefaultLayerBlendMode;
        List<CinematicMaterialLayer> layers = new(LayerCount);

        for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            if (reader.Remaining < 4)
                break;

            int layerSize = reader.ReadInt32();
            if (layerSize is not (MaterialLayerWithAlphaThresholdSerializedSize or MaterialLayerWithoutAlphaThresholdSerializedSize))
                throw new InvalidDataException($"Invalid legacy material layer marker 0x{layerSize:X8} at 0x{reader.Offset - 4:X}.");

            layers.Add(ReadLayer(reader, layerBlendMode, layerSize));

            if (layerIndex < LayerCount - 1)
            {
                if (reader.Remaining < 4)
                    break;

                layerBlendMode = reader.ReadInt32();
            }
        }

        if (layers.Count == 0)
            return CinematicRenderMaterial.Empty;

        if (layers.All(layer => !layer.Enabled))
        {
            CinematicMaterialLayer layer0 = layers[0];
            return new CinematicRenderMaterial(
                blendMode,
                [
                    layer0 with
                    {
                        Enabled = true,
                        BlendMode = DefaultLayerBlendMode,
                        TextureUsage = CinematicTextureUsage.EntireTexture,
                        DiffuseColor = CinematicMaterialColor.White
                    }
                ]);
        }

        return new CinematicRenderMaterial(blendMode, layers);
    }

    private static bool IsJsonMaterial(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (char.IsWhiteSpace((char)value) || value == 0)
                continue;

            return value == (byte)'{';
        }

        return false;
    }

    private static CinematicRenderMaterial ReadJson(byte[] bytes)
    {
        int length = bytes.Length;
        while (length > 0 && (bytes[length - 1] == 0 || char.IsWhiteSpace((char)bytes[length - 1])))
            length--;

        using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(0, length));
        JsonElement root = document.RootElement;
        int blendMode = ReadJsonInt(root, "blendmode", DefaultLayerBlendMode);
        int layerBlendMode = DefaultLayerBlendMode;
        List<CinematicMaterialLayer> layers = new(LayerCount);

        for (int layerIndex = 1; layerIndex <= LayerCount; layerIndex++)
        {
            if (layerIndex > 1)
                layerBlendMode = ReadJsonInt(root, $"BlendLayer{layerIndex}", DefaultLayerBlendMode);

            if (!root.TryGetProperty($"Layer{layerIndex}", out JsonElement layerElement) ||
                layerElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            layers.Add(ReadJsonLayer(layerElement, layerBlendMode));
        }

        return layers.Count == 0
            ? CinematicRenderMaterial.Empty
            : new CinematicRenderMaterial(blendMode, layers);
    }

    private static CinematicMaterialLayer ReadJsonLayer(JsonElement element, int blendMode)
    {
        IReadOnlyList<CinematicUvModifier> modifiers = ReadJsonUvModifiers(element);
        return new CinematicMaterialLayer(
            ReadJsonBool(element, "Enabled", true),
            ReadAddressMode(ReadJsonInt(element, "TexAdressingModeU", ReadJsonInt(element, "TexAddressingModeU", 0))),
            ReadAddressMode(ReadJsonInt(element, "TexAdressingModeV", ReadJsonInt(element, "TexAddressingModeV", 0))),
            modifiers,
            blendMode,
            ReadTextureUsage(ReadJsonInt(element, "TextureUsage", (int)CinematicTextureUsage.EntireTexture)),
            ReadJsonDiffuseColor(element));
    }

    private static IReadOnlyList<CinematicUvModifier> ReadJsonUvModifiers(JsonElement layerElement)
    {
        if (!layerElement.TryGetProperty("UVModifiers", out JsonElement modifiersElement) ||
            modifiersElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<CinematicUvModifier> modifiers = [];
        foreach (JsonElement modifierElement in modifiersElement.EnumerateArray())
        {
            if (modifierElement.ValueKind != JsonValueKind.Object)
                continue;

            modifiers.Add(new CinematicUvModifier(
                ReadJsonFloat(modifierElement, "TranslationU", 0.0f),
                ReadJsonFloat(modifierElement, "TranslationV", 0.0f),
                ReadJsonBool(modifierElement, "AnimTranslationU", false),
                ReadJsonBool(modifierElement, "AnimTranslationV", false),
                ReadJsonFloat(modifierElement, "Rotation", 0.0f),
                ReadJsonFloat(modifierElement, "RotationOffsetU", 0.0f),
                ReadJsonFloat(modifierElement, "RotationOffsetV", 0.0f),
                ReadJsonBool(modifierElement, "AnimRotation", false),
                ReadJsonFloat(modifierElement, "ScaleU", 1.0f),
                ReadJsonFloat(modifierElement, "ScaleV", 1.0f),
                ReadJsonFloat(modifierElement, "ScaleOffsetU", 0.0f),
                ReadJsonFloat(modifierElement, "ScaleOffsetV", 0.0f)));
        }

        return modifiers;
    }

    private static CinematicMaterialColor ReadJsonDiffuseColor(JsonElement element)
    {
        if (!element.TryGetProperty("DiffuseColor", out JsonElement colorElement) ||
            colorElement.ValueKind != JsonValueKind.Array)
        {
            return CinematicMaterialColor.White;
        }

        float[] values =
        [
            .. colorElement
                .EnumerateArray()
                .Take(4)
                .Select(value => value.TryGetSingle(out float parsed) && float.IsFinite(parsed) ? parsed : 1.0f)
        ];
        return values.Length >= 4
            ? new CinematicMaterialColor(values[0], values[1], values[2], values[3])
            : CinematicMaterialColor.White;
    }

    private static int ReadJsonInt(JsonElement element, string propertyName, int defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return defaultValue;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out int value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) => value,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => defaultValue
        };
    }

    private static float ReadJsonFloat(JsonElement element, string propertyName, float defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return defaultValue;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetSingle(out float value) && float.IsFinite(value) => value,
            JsonValueKind.String when float.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) => value,
            _ => defaultValue
        };
    }

    private static bool ReadJsonBool(JsonElement element, string propertyName, bool defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return defaultValue;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when property.TryGetInt32(out int value) => value != 0,
            JsonValueKind.String when bool.TryParse(property.GetString(), out bool value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) => value != 0,
            _ => defaultValue
        };
    }

    public static bool TryCreateShaderFallback(string? shaderPath, out CinematicRenderMaterial material)
    {
        material = CinematicRenderMaterial.Empty;
        if (string.IsNullOrWhiteSpace(shaderPath) ||
            !Path.GetExtension(shaderPath).Equals(".msh", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string shaderName = Path.GetFileNameWithoutExtension(shaderPath).ToLowerInvariant();
        if (shaderName == "pleoalpha")
        {
            material = new CinematicRenderMaterial(
                GfxBlendAlpha,
                [
                    CinematicMaterialLayer.Default with
                    {
                        AddressModeU = TextureAddressMode.Clamp,
                        AddressModeV = TextureAddressMode.Clamp,
                        TextureUsage = CinematicTextureUsage.PleoStackedAlpha
                    }
                ]);
            return true;
        }

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

        material = new CinematicRenderMaterial(blendMode, [CinematicMaterialLayer.Default]);
        return true;
    }

    internal static CinematicRenderMaterial ApplyMaterialPathConventions(
        string? materialPath,
        CinematicRenderMaterial material)
    {
        if (string.IsNullOrWhiteSpace(materialPath) ||
            !Path.GetFileNameWithoutExtension(materialPath).EndsWith("_shadow", StringComparison.OrdinalIgnoreCase) ||
            !HasChannelAlphaMaskColorLayerPattern(material))
        {
            return material;
        }

        CinematicMaterialLayer[] layers = [.. material.Layers];
        CinematicMaterialLayer colorLayer = layers[1];
        CinematicMaterialColor color = colorLayer.DiffuseColor ?? CinematicMaterialColor.White;
        layers[1] = colorLayer with
        {
            DiffuseColor = new CinematicMaterialColor(
                color.Red,
                color.Green,
                color.Blue,
                color.Alpha * AnimatedShadowLayerAlpha)
        };
        return material with { Layers = layers };
    }

    private static CinematicMaterialLayer ReadLayer(CinematicBinaryReader reader, int blendMode, int layerSize)
    {
        int startOffset = reader.Offset;
        if (layerSize == MaterialLayerWithAlphaThresholdSerializedSize &&
            TryReadLayer(reader, blendMode, hasAlphaThreshold: false, out CinematicMaterialLayer? sourceLayer))
        {
            return sourceLayer!;
        }

        reader.Offset = startOffset;
        bool hasAlphaThreshold = layerSize == MaterialLayerWithAlphaThresholdSerializedSize;
        if (TryReadLayer(reader, blendMode, hasAlphaThreshold, out CinematicMaterialLayer? layer))
            return layer!;

        reader.Offset = startOffset;
        throw new InvalidDataException($"Invalid legacy material layer at 0x{startOffset:X}.");
    }

    private static bool TryReadLayer(
        CinematicBinaryReader reader,
        int blendMode,
        bool hasAlphaThreshold,
        out CinematicMaterialLayer? layer)
    {
        int startOffset = reader.Offset;
        try
        {
            layer = ReadLayerPayload(reader, blendMode, hasAlphaThreshold);
            return true;
        }
        catch (InvalidDataException)
        {
            reader.Offset = startOffset;
            layer = null;
            return false;
        }
    }

    private static CinematicMaterialLayer ReadLayerPayload(
        CinematicBinaryReader reader,
        int blendMode,
        bool hasAlphaThreshold)
    {
        bool enabled = reader.ReadUInt32() != 0;
        if (hasAlphaThreshold)
            reader.Skip(sizeof(float));

        TextureAddressMode addressU = ReadAddressMode(reader.ReadInt32());
        TextureAddressMode addressV = ReadAddressMode(reader.ReadInt32());
        ReadBooleanMarker(reader);
        float serializedBlue = reader.ReadSingle();
        float serializedGreen = reader.ReadSingle();
        float serializedRed = reader.ReadSingle();
        float serializedAlpha = reader.ReadSingle();
        CinematicMaterialColor diffuseColor = new(
            serializedRed,
            serializedGreen,
            serializedBlue,
            serializedAlpha);
        CinematicTextureUsage textureUsage = ReadTextureUsage(reader.ReadInt32());

        int uvModifierCount = reader.ReadInt32();
        if (uvModifierCount is < 0 or > 1024)
            throw new InvalidDataException($"Invalid legacy UV modifier count {uvModifierCount} at 0x{reader.Offset - 4:X}.");

        List<CinematicUvModifier> modifiers = new(uvModifierCount);
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

        return new CinematicMaterialLayer(enabled, addressU, addressV, modifiers, blendMode, textureUsage, diffuseColor);
    }

    private static void ReadBooleanMarker(CinematicBinaryReader reader)
    {
        uint raw = reader.ReadUInt32();
        if (raw is not (0 or 1))
            throw new InvalidDataException($"Invalid legacy material boolean marker 0x{raw:X8} at 0x{reader.Offset - 4:X}.");
    }

    private static CinematicUvModifier ReadUvModifier(CinematicBinaryReader reader) =>
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

    private static bool HasChannelAlphaMaskColorLayerPattern(CinematicRenderMaterial material)
    {
        if (material.BlendMode != GfxBlendAlpha ||
            material.Layers.Count < 2 ||
            !material.Layers[0].Enabled ||
            !material.Layers[1].Enabled ||
            material.Layers[1].BlendMode != GfxBlendAlpha)
        {
            return false;
        }

        bool maskLayer =
            material.Layers[0].TextureUsage is
                CinematicTextureUsage.AlphaIsRed or
                CinematicTextureUsage.AlphaIsGreen or
                CinematicTextureUsage.AlphaIsBlue or
                CinematicTextureUsage.AlphaIsAlpha;
        bool colorLayer =
            material.Layers[1].TextureUsage is
                CinematicTextureUsage.EntireTexture or
                CinematicTextureUsage.RgbOnly or
                CinematicTextureUsage.PleoStackedAlpha;
        return maskLayer && colorLayer;
    }

    private static TextureAddressMode ReadAddressMode(int raw) =>
        Enum.IsDefined(typeof(TextureAddressMode), raw)
            ? (TextureAddressMode)raw
            : TextureAddressMode.Wrap;

    private static CinematicTextureUsage ReadTextureUsage(int raw) =>
        Enum.IsDefined(typeof(CinematicTextureUsage), raw)
            ? (CinematicTextureUsage)raw
            : CinematicTextureUsage.EntireTexture;
}
