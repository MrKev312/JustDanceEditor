using JustDanceEditor.Formats.UbiArt.Files;

using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace JustDanceEditor.Formats.UbiArt;

public static class ISC
{
    public static bool GetActorPath(string input, string actorName, [MaybeNullWhen(false)] out string actorPath)
    {
        actorPath = null;

        if (!Path.Exists(input))
            return false;

        // Try to read as XML first (text-based ISC)
        try
        {
            string text = File.ReadAllText(input);
            XDocument xmlDoc = XDocument.Parse(text);
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
        }
        catch
        {
            // Not XML or failed to parse - could be binary in future. For now return false.
        }

        return false;
    }

    public static bool GetActorPath(CookedFile cookedFile, string actorName, [MaybeNullWhen(false)] out string actorPath)
    {
        return GetActorPath(cookedFile.FullPath, actorName, out actorPath);
    }
}