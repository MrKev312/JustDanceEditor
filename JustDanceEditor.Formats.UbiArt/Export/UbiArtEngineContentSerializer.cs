using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using System.Reflection;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Export;

public static class UbiArtEngineContentSerializer
{
    public static byte[] Serialize(object? content)
    {
        if (content == null)
            return [];

        if (content is byte[] bytes)
            return bytes;

        if (content is ReadOnlyMemory<byte> readOnlyMemory)
            return readOnlyMemory.ToArray();

        if (content is Memory<byte> memory)
            return memory.ToArray();

        if (content is string text)
            return Encoding.UTF8.GetBytes(text);

        if (CanSerializeLegacyBinary(content.GetType()))
            return LegacyBinarySerializer.Serialize(content);

        throw new InvalidOperationException($"Generated UbiArt content of type '{content.GetType().FullName}' is not serializable.");
    }

    private static bool CanSerializeLegacyBinary(Type type)
    {
        if (type == typeof(LegacyPadding) ||
            type == typeof(LegacyUbiArtPath) ||
            type == typeof(LegacyBinarySequence))
        {
            return true;
        }

        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            if (current.GetProperties(flags).Any(property => property.GetCustomAttribute<LegacyBinaryFieldAttribute>() != null) ||
                current.GetFields(flags).Any(field => field.GetCustomAttribute<LegacyBinaryFieldAttribute>() != null))
            {
                return true;
            }
        }

        return false;
    }
}
