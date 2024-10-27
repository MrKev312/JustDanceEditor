using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace JustDanceEditor.Converter.UbiArt;

public static class ISC
{
    public static bool GetActorPath(string input, string actorName, [MaybeNullWhen(false)] out string actorPath)
    {
        actorPath = null;

        // Return false if the actor path is not found
        if (Path.Exists(input) == false)
            return false;

        // Load the XML from the file
        XDocument xmlDoc = XDocument.Load(input);

        foreach (XElement actor in xmlDoc.Descendants("Actor"))
        {
            XAttribute? attribute = actor.Attribute("USERFRIENDLY");
            if (attribute != null && string.Equals(attribute.Value, actorName, StringComparison.OrdinalIgnoreCase))
            {
                XAttribute? luaAttribute = actor.Attribute("LUA");
                if (luaAttribute != null)
                {
                    actorPath = luaAttribute.Value;
                    return true;
                }
            }
        }

        return false;
    }
}