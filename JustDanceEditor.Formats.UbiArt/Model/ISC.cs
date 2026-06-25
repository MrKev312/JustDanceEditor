using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.FileSystem;

using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace JustDanceEditor.Formats.UbiArt.Model;

public static class ISC
{
    public static bool GetActorPath(CookedFile cookedFile, string actorName, [MaybeNullWhen(false)] out string actorPath, JustDanceUbiArtFileSystem? fileSystem = null)
    {
        actorPath = null;

        if (fileSystem == null)
            throw new ArgumentNullException(nameof(fileSystem), "JustDanceUbiArtFileSystem is required; use the stream-based overload.");

        try
        {
            using Stream s = fileSystem.GetFileStream(cookedFile);
            XDocument xmlDoc = XDocument.Load(s);
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
            return false;
        }

        return false;
    }
}