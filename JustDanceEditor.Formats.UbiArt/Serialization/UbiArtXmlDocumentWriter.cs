using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace JustDanceEditor.Formats.UbiArt.Serialization;

/// <summary>
/// Small object model and serializer for UbiArt's attribute-heavy XML resources.
/// </summary>
public static class UbiArtXmlDocumentWriter
{
    public static UbiArtXmlNode Node(string name, object? attributes = null, params UbiArtXmlNode[] children) =>
        new(name, GetAttributes(attributes), children);

    public static byte[] Write(UbiArtXmlNode root)
    {
        XDocument document = new(new XDeclaration("1.0", "ISO-8859-1", null), ToElement(root));
        using MemoryStream stream = new();
        XmlWriterSettings settings = new()
        {
            Encoding = Encoding.Latin1,
            Indent = true,
            IndentChars = "\t",
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false
        };
        using (XmlWriter writer = XmlWriter.Create(stream, settings))
            document.Save(writer);
        return stream.ToArray();
    }

    public static UbiArtXmlNode Read(byte[] document)
    {
        using MemoryStream stream = new(document, writable: false);
        XDocument parsed = XDocument.Load(stream);
        return FromElement(parsed.Root ?? throw new InvalidDataException("XML document has no root element."));
    }

    public static UbiArtXmlNode Scene(IEnumerable<UbiArtXmlNode> children, bool viewFamily = false)
        => Scene(children, new UbiArtSceneSettings(ViewFamily: viewFamily ? 1 : null));

    public static UbiArtXmlNode Scene(IEnumerable<UbiArtXmlNode> children, UbiArtSceneSettings settings)
    {
        Dictionary<string, object?> attributes = new(StringComparer.Ordinal)
        {
            ["ENGINE_VERSION"] = settings.EngineVersion,
            ["GRIDUNIT"] = settings.GridUnit,
            ["DEPTH_SEPARATOR"] = "0",
            ["NEAR_SEPARATOR"] = MatrixIdentity,
            ["FAR_SEPARATOR"] = MatrixIdentity
        };
        if (settings.ViewFamily.HasValue)
            attributes["viewFamily"] = settings.ViewFamily.Value;
        if (settings.IsPopup.HasValue)
            attributes["isPopup"] = settings.IsPopup.Value;

        return Node("root", null, new UbiArtXmlNode("Scene", attributes, [.. children]));
    }

    public static UbiArtXmlNode DefaultSceneConfig() =>
        Node("sceneConfigs", null, Node("SceneConfigs", new { activeSceneConfig = 0 }));

    public static UbiArtXmlNode Actor(string type, object attributes, params UbiArtXmlNode[] children) =>
        Node("ACTORS", new { NAME = type }, Node(type, attributes, children));

    public static UbiArtXmlNode Component(string name, params UbiArtXmlNode[] content) =>
        Node("COMPONENTS", new { NAME = name }, Node(name, null, content));

    public static object StandardActorAttributes(string userFriendly, string lua, string instanceDataFile = "", string scale = "1.000000 1.000000", string position = "0.000000 0.000000") => new
    {
        RELATIVEZ = "0.000000",
        SCALE = scale,
        xFLIPPED = "0",
        USERFRIENDLY = userFriendly,
        POS2D = position,
        ANGLE = "0.000000",
        INSTANCEDATAFILE = instanceDataFile,
        LUA = lua
    };

    private static IReadOnlyDictionary<string, object?> GetAttributes(object? attributes)
    {
        if (attributes == null)
            return new Dictionary<string, object?>();
        if (attributes is IReadOnlyDictionary<string, object?> readOnlyDictionary)
            return readOnlyDictionary;
        if (attributes is IDictionary<string, object?> dictionary)
            return new Dictionary<string, object?>(dictionary, StringComparer.Ordinal);

        return attributes.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, property => property.GetValue(attributes), StringComparer.Ordinal);
    }

    private static XElement ToElement(UbiArtXmlNode node)
    {
        XElement element = new(node.Name);
        foreach ((string name, object? value) in node.Attributes)
        {
            if (value != null)
                element.SetAttributeValue(name, Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        foreach (UbiArtXmlNode child in node.Children)
            element.Add(ToElement(child));
        return element;
    }

    private static UbiArtXmlNode FromElement(XElement element) => new(
        element.Name.LocalName,
        element.Attributes().ToDictionary(attribute => attribute.Name.LocalName, attribute => (object?)attribute.Value, StringComparer.Ordinal),
        [.. element.Elements().Select(FromElement)]);

    private const string MatrixIdentity = "1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000";
}

public sealed record UbiArtXmlNode(
    string Name,
    IReadOnlyDictionary<string, object?> Attributes,
    IReadOnlyList<UbiArtXmlNode> Children);

public sealed record UbiArtSceneSettings(
    string EngineVersion = "55299",
    string GridUnit = "0.500000",
    int? ViewFamily = null,
    int? IsPopup = null);
