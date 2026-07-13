using JustDanceEditor.Formats.UbiArt.Serialization;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class DocumentWriterTests
{
    [Fact]
    public void LuaWriter_SerializesObjectsCollectionsExpressionsAndEscapedStrings()
    {
        object value = new
        {
            Text = "quote \" slash \\ line\nnext",
            Mode = new LuaExpression("GameMode.Classic"),
            Values = new[] { 1, 2, 3 },
            Empty = Array.Empty<object>()
        };

        string lua = LuaDocumentWriter.Write(
            value,
            includes: ["EngineData/Helpers/SongDatabase.ilu"],
            trailingStatements: ["afterWrite(params)"]);

        Assert.Contains("includeReference(\"EngineData/Helpers/SongDatabase.ilu\")", lua);
        Assert.Contains("Text = \"quote \\\" slash \\\\ line\\nnext\"", lua);
        Assert.Contains("Mode = GameMode.Classic", lua);
        Assert.Contains("Values =", lua);
        Assert.Contains("Empty = {}", lua);
        Assert.EndsWith("afterWrite(params)" + Environment.NewLine, lua, StringComparison.Ordinal);

        JsonElement parsed = LuaTableSerializer.Deserialize<JsonElement>(lua.Replace("afterWrite(params)", ""));
        Assert.Equal("quote \" slash \\ line\nnext", parsed.GetProperty("Text").GetString());
    }

    [Fact]
    public void XmlWriter_SerializesAndReadsObjectNodeTreesAsLatin1()
    {
        UbiArtXmlNode document = UbiArtXmlDocumentWriter.Scene(
            [
                UbiArtXmlDocumentWriter.Actor(
                    "Actor",
                    new { USERFRIENDLY = "Café", DEFAULTENABLE = 1 },
                    UbiArtXmlDocumentWriter.Component("ExampleComponent")),
                UbiArtXmlDocumentWriter.DefaultSceneConfig()
            ],
            new UbiArtSceneSettings("326704", "0.500000", ViewFamily: 0, IsPopup: 0));

        byte[] bytes = UbiArtXmlDocumentWriter.Write(document);
        using MemoryStream stream = new(bytes);
        XDocument parsed = XDocument.Load(stream);

        Assert.Equal("iso-8859-1", parsed.Declaration?.Encoding, ignoreCase: true);
        Assert.Equal("Café", parsed.Descendants("Actor").Single().Attribute("USERFRIENDLY")?.Value);
        Assert.Contains((byte)0xE9, bytes);
        Assert.Equal("root", UbiArtXmlDocumentWriter.Read(bytes).Name);
    }
}