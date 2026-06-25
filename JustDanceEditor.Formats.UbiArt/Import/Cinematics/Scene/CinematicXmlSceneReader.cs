using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;

using Microsoft.Extensions.Logging;

using System.Globalization;
using System.Xml.Linq;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicXmlSceneReader
{
    public static bool IsXmlScene(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (char.IsWhiteSpace((char)value))
                continue;

            return value == (byte)'<';
        }

        return false;
    }

    public static IReadOnlyList<CinematicActor> ReadScene(
        JustDanceUbiArtFileSystem fileSystem,
        byte[] bytes,
        IReadOnlyList<string> parentPath,
        ILogger logger)
    {
        using MemoryStream stream = new(bytes);
        XDocument document = XDocument.Load(stream, LoadOptions.None);
        XElement? scene = document.Root?.Element("Scene") ?? document.Root;
        if (scene == null || !IsElement(scene, "Scene"))
            return [];

        List<CinematicActor> actors = [];
        ReadSceneElement(fileSystem, scene, parentPath, actors, logger);
        return actors;
    }

    private static void ReadSceneElement(
        JustDanceUbiArtFileSystem fileSystem,
        XElement scene,
        IReadOnlyList<string> parentPath,
        List<CinematicActor> actors,
        ILogger logger)
    {
        int siblingOrder = 0;
        foreach (XElement actorsElement in scene.Elements().Where(element => IsElement(element, "ACTORS")))
        {
            XElement? actorElement = actorsElement.Elements().FirstOrDefault(IsActorElement);
            if (actorElement == null)
                continue;

            CinematicActor actor = CreateActor(actorElement, parentPath, siblingOrder++);
            actors.Add(actor);

            foreach (XElement embeddedScene in actorElement
                .Elements()
                .Where(element => IsElement(element, "SCENE"))
                .Elements()
                .Where(element => IsElement(element, "Scene")))
            {
                ReadSceneElement(fileSystem, embeddedScene, actor.Path, actors, logger);
            }
        }
    }

    private static CinematicActor CreateActor(
        XElement actorElement,
        IReadOnlyList<string> parentPath,
        int siblingOrder)
    {
        bool isSubScene = IsElement(actorElement, "SubSceneActor");
        string name = GetString(actorElement, "USERFRIENDLY");
        if (string.IsNullOrWhiteSpace(name))
            name = $"{actorElement.Name.LocalName}_{siblingOrder}";

        string templatePath = CinematicNames.NormalizePath(GetString(actorElement, "LUA"));
        string? subScenePath = isSubScene
            ? NullIfWhiteSpace(CinematicNames.NormalizePath(GetString(actorElement, "RELATIVEPATH")))
            : null;
        (float scaleX, float scaleY) = ParsePair(GetString(actorElement, "SCALE"), 1.0f, 1.0f);
        (float positionX, float positionY) = ParsePair(GetString(actorElement, "POS2D"), 0.0f, 0.0f);
        XmlVisualInfo visual = ReadVisualInfo(actorElement);
        string[] actorPath = [.. parentPath, name];

        return new CinematicActor(
            actorPath,
            name,
            SourceOffset: -1,
            siblingOrder,
            isSubScene
                ? LegacyBinarySerializer.GetTypeId<CinematicSubSceneActorBinary>()
                : LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
            GetFloat(actorElement, "RELATIVEZ", 0.0f),
            scaleX,
            scaleY,
            GetUInt(actorElement, "xFLIPPED", 0),
            GetAngleRadians(actorElement, "ANGLE", 0.0f),
            positionX,
            positionY,
            templatePath,
            subScenePath,
            visual.TexturePath,
            visual.TexturePaths,
            visual.MaterialPath,
            visual.ExplicitAtlasPath,
            visual.MeshPath,
            visual.VisualComponentTypeId,
            visual.AtlasIndex,
            visual.AtlasTextureSlot,
            visual.Anchor,
            visual.CustomAnchorX,
            visual.CustomAnchorY,
            ScenePriority: 0,
            GetBool(actorElement, "DEFAULTENABLE", true),
            visual.BaseTint,
            visual.BaseAlpha,
            ParentBind: null,
            visual.Sinus,
            MeshInitialScaleZ: visual.MeshInitialScaleZ,
            MeshOrientation: visual.MeshOrientation,
            MeshForce2DRender: visual.MeshForce2DRender,
            PleoVideoPath: visual.PleoVideoPath);
    }

    private static XmlVisualInfo ReadVisualInfo(XElement actorElement)
    {
        XElement? component = actorElement
            .Elements()
            .Where(element => IsElement(element, "COMPONENTS"))
            .Elements()
            .FirstOrDefault(element =>
                IsElement(element, "PleoComponent") ||
                IsElement(element, "PleoTextureGraphicComponent") ||
                IsElement(element, "MaterialGraphicComponent") ||
                IsElement(element, "Mesh3DComponent"));
        if (component == null)
            return XmlVisualInfo.Empty;

        string? pleoVideoPath = ReadPleoVideoPath(component);
        uint? visualComponentTypeId = GetVisualComponentTypeId(component);
        IReadOnlyList<string> texturePaths = ReadTexturePaths(actorElement, component);
        string? texturePath = texturePaths.FirstOrDefault();
        string? materialPath = ReadMaterialPath(component);
        string? explicitAtlasPath = ReadAtlasPath(component);
        string? meshPath = ReadMeshPath(component);
        float meshInitialScaleZ = IsElement(component, "Mesh3DComponent")
            ? GetFloat(component, "ScaleZ", 0.0f)
            : 0.0f;
        CinematicMeshOrientation meshOrientation = IsElement(component, "Mesh3DComponent")
            ? ReadMeshOrientation(component)
            : default;
        bool meshForce2DRender = GetBool(component, "force2DRender", false);
        int atlasIndex = GetInt(component, "AtlasIndex", 0);
        int atlasTextureSlot = ReadAtlasTextureSlot(component);
        (float customAnchorX, float customAnchorY) = ParsePair(GetString(component, "customAnchor"), 0.0f, 0.0f);
        TextureAnchor anchor = ReadAnchor(component);
        RgbTint baseTint = ReadBaseTint(component);
        float baseAlpha = ReadBaseAlpha(component);
        CinematicSinusParameters sinus = ReadSinus(component);

        return new XmlVisualInfo(
            texturePath,
            texturePaths,
            materialPath,
            explicitAtlasPath,
            meshPath,
            visualComponentTypeId,
            atlasIndex,
            atlasTextureSlot,
            anchor,
            customAnchorX,
            customAnchorY,
            baseTint,
            baseAlpha,
            sinus,
            meshInitialScaleZ,
            meshOrientation,
            meshForce2DRender,
            pleoVideoPath);
    }

    private static string? ReadPleoVideoPath(XElement component)
    {
        if (!IsElement(component, "PleoComponent"))
            return null;

        string path = GetString(component, "video");
        if (string.IsNullOrWhiteSpace(path))
            path = GetString(component, "dashMPD");

        return NullIfWhiteSpace(CinematicNames.NormalizePath(path));
    }

    private static uint? GetVisualComponentTypeId(XElement component)
    {
        if (IsElement(component, "PleoTextureGraphicComponent"))
            return LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>();

        if (IsElement(component, "MaterialGraphicComponent"))
            return LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicComponentBinary>();

        if (IsElement(component, "Mesh3DComponent"))
            return LegacyBinarySerializer.GetTypeId<CinematicMesh3DComponentBinary>();

        return null;
    }

    private static IReadOnlyList<string> ReadTexturePaths(XElement actorElement, XElement component)
    {
        XElement? textureSet = component.Descendants().FirstOrDefault(element => IsElement(element, "GFXMaterialTexturePathSet"));
        string[] textureSlots =
        [
            "diffuse",
            "diffuse_2",
            "diffuse_3",
            "diffuse_4"
        ];

        string?[] slotPaths = new string?[textureSlots.Length];
        if (textureSet != null)
        {
            for (int index = 0; index < textureSlots.Length; index++)
                slotPaths[index] = NullIfWhiteSpace(CinematicNames.NormalizePath(GetString(textureSet, textureSlots[index])));
        }

        ApplyTexturePatcher(actorElement, slotPaths);

        List<string> paths = [];
        foreach (string? path in slotPaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }

        if (textureSet == null)
            return paths;

        string[] additionalSlots =
        [
            "back_light",
            "back_light_2",
            "normal",
            "separateAlpha",
            "anim_impostor"
        ];
        foreach (string slot in additionalSlots)
        {
            string value = CinematicNames.NormalizePath(GetString(textureSet, slot));
            if (!string.IsNullOrWhiteSpace(value))
                paths.Add(value);
        }

        return paths;
    }

    private static void ApplyTexturePatcher(XElement actorElement, string?[] texturePaths)
    {
        XElement? patcher = actorElement
            .Elements()
            .Where(element => IsElement(element, "COMPONENTS"))
            .Elements()
            .FirstOrDefault(element => IsElement(element, "TexturePatcherComponent"));
        if (patcher == null)
            return;

        for (int index = 0; index < texturePaths.Length; index++)
        {
            string patchedPath = CinematicNames.NormalizePath(GetString(patcher, $"Diffuse{index + 1}"));
            if (!string.IsNullOrWhiteSpace(patchedPath))
                texturePaths[index] = patchedPath;
        }
    }

    private static string? ReadMeshPath(XElement component)
    {
        string meshPath = GetString(component, "mesh3D");
        if (string.IsNullOrWhiteSpace(meshPath))
            meshPath = GetString(component, "mesh");

        return NullIfWhiteSpace(CinematicNames.NormalizePath(meshPath));
    }

    private static CinematicMeshOrientation ReadMeshOrientation(XElement component)
    {
        float[] values = ParseFloatList(GetString(component, "orientation"), 16);
        if (values.Length < 11)
            return default;

        return new CinematicMeshOrientation(
            values[0],
            values[1],
            values[2],
            values[4],
            values[5],
            values[6],
            values[8],
            values[9],
            values[10],
            HasValue: true);
    }

    private static string? ReadMaterialPath(XElement component)
    {
        XElement? material = component.Descendants().FirstOrDefault(element => IsElement(element, "GFXMaterialSerializable"));
        if (material == null)
            return null;

        return NullIfWhiteSpace(CinematicNames.NormalizePath(GetString(material, "shaderPath")));
    }

    private static string? ReadAtlasPath(XElement component)
    {
        XElement? material = component.Descendants().FirstOrDefault(element => IsElement(element, "GFXMaterialSerializable"));
        if (material == null)
            return null;

        return NullIfWhiteSpace(CinematicNames.NormalizePath(GetString(material, "ATL_Path")));
    }

    private static int ReadAtlasTextureSlot(XElement component)
    {
        XElement? material = component.Descendants().FirstOrDefault(element => IsElement(element, "GFXMaterialSerializable"));
        if (material == null)
            return 0;

        int value = GetInt(material, "ATL_Channel", 0);
        return value is >= 0 and <= 3 ? value : 0;
    }

    private static TextureAnchor ReadAnchor(XElement component)
    {
        XElement? anchorEnum = component
            .Descendants()
            .FirstOrDefault(element =>
                IsElement(element, "ENUM") &&
                string.Equals(GetString(element, "NAME"), "anchor", StringComparison.OrdinalIgnoreCase));
        int value = anchorEnum == null
            ? (int)TextureAnchor.MiddleCenter
            : GetInt(anchorEnum, "SEL", (int)TextureAnchor.MiddleCenter);
        return Enum.IsDefined(typeof(TextureAnchor), value)
            ? (TextureAnchor)value
            : TextureAnchor.MiddleCenter;
    }

    private static RgbTint ReadBaseTint(XElement component)
    {
        XElement? primitive = component.Descendants().FirstOrDefault(element => IsElement(element, "GFXPrimitiveParam"));
        (float red, float green, float blue, _) = ParseQuad(GetString(primitive, "colorFactor"), 1.0f, 1.0f, 1.0f, 1.0f);
        return new RgbTint(red, green, blue);
    }

    private static float ReadBaseAlpha(XElement component)
    {
        XElement? primitive = component.Descendants().FirstOrDefault(element => IsElement(element, "GFXPrimitiveParam"));
        (_, _, _, float alpha) = ParseQuad(GetString(primitive, "colorFactor"), 1.0f, 1.0f, 1.0f, 1.0f);
        return Math.Clamp(alpha, 0.0f, 1.0f);
    }

    private static CinematicSinusParameters ReadSinus(XElement component)
    {
        (float amplitudeX, float amplitudeY, float amplitudeZ) = ParseTriple(GetString(component, "SinusAmplitude"), 0.0f, 0.0f, 0.0f);
        return new CinematicSinusParameters(
            amplitudeX,
            amplitudeY,
            amplitudeZ,
            GetFloat(component, "SinusSpeed", 1.0f),
            GetAngleRadians(component, "AngleX", 0.0f),
            GetAngleRadians(component, "AngleY", 0.0f));
    }

    private static bool IsActorElement(XElement element) =>
        IsElement(element, "Actor") || IsElement(element, "SubSceneActor");

    private static bool IsElement(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static string GetString(XElement? element, string attributeName) =>
        element?.Attribute(attributeName)?.Value ?? string.Empty;

    private static float GetFloat(XElement element, string attributeName, float defaultValue) =>
        float.TryParse(GetString(element, attributeName), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
            float.IsFinite(value)
                ? value
                : defaultValue;

    private static float GetAngleRadians(XElement element, string attributeName, float defaultDegrees)
    {
        float degrees = GetFloat(element, attributeName, defaultDegrees);
        return degrees * MathF.PI / 180.0f;
    }

    private static int GetInt(XElement element, string attributeName, int defaultValue) =>
        int.TryParse(GetString(element, attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : defaultValue;

    private static uint GetUInt(XElement element, string attributeName, uint defaultValue) =>
        uint.TryParse(GetString(element, attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value)
            ? value
            : defaultValue;

    private static bool GetBool(XElement element, string attributeName, bool defaultValue)
    {
        string value = GetString(element, attributeName);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
            return intValue != 0;

        return bool.TryParse(value, out bool boolValue) ? boolValue : defaultValue;
    }

    private static (float X, float Y) ParsePair(string value, float defaultX, float defaultY)
    {
        float[] values = ParseFloatList(value, 2);
        return values.Length >= 2
            ? (values[0], values[1])
            : (defaultX, defaultY);
    }

    private static (float X, float Y, float Z) ParseTriple(string value, float defaultX, float defaultY, float defaultZ)
    {
        float[] values = ParseFloatList(value, 3);
        return values.Length >= 3
            ? (values[0], values[1], values[2])
            : (defaultX, defaultY, defaultZ);
    }

    private static (float X, float Y, float Z, float W) ParseQuad(string value, float defaultX, float defaultY, float defaultZ, float defaultW)
    {
        float[] values = ParseFloatList(value, 4);
        return values.Length >= 4
            ? (values[0], values[1], values[2], values[3])
            : (defaultX, defaultY, defaultZ, defaultW);
    }

    private static float[] ParseFloatList(string value, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return
        [
            .. value
                .Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Take(maxCount)
                .Select(part => float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed)
                    ? parsed
                    : float.NaN)
                .Where(float.IsFinite)
        ];
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private readonly record struct XmlVisualInfo(
        string? TexturePath,
        IReadOnlyList<string> TexturePaths,
        string? MaterialPath,
        string? ExplicitAtlasPath,
        string? MeshPath,
        uint? VisualComponentTypeId,
        int AtlasIndex,
        int AtlasTextureSlot,
        TextureAnchor Anchor,
        float CustomAnchorX,
        float CustomAnchorY,
        RgbTint BaseTint,
        float BaseAlpha,
        CinematicSinusParameters Sinus,
        float MeshInitialScaleZ,
        CinematicMeshOrientation MeshOrientation,
        bool MeshForce2DRender,
        string? PleoVideoPath)
    {
        public static XmlVisualInfo Empty { get; } = new(
            TexturePath: null,
            TexturePaths: [],
            MaterialPath: null,
            ExplicitAtlasPath: null,
            MeshPath: null,
            VisualComponentTypeId: null,
            AtlasIndex: 0,
            AtlasTextureSlot: 0,
            Anchor: TextureAnchor.MiddleCenter,
            CustomAnchorX: 0.0f,
            CustomAnchorY: 0.0f,
            BaseTint: RgbTint.White,
            BaseAlpha: 1.0f,
            Sinus: default,
            MeshInitialScaleZ: 0.0f,
            MeshOrientation: default,
            MeshForce2DRender: false,
            PleoVideoPath: null);
    }
}