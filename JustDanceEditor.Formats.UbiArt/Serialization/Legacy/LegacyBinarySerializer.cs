using System.Collections;
using System.IO.Hashing;
using System.Reflection;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal static class LegacyBinarySerializer
{
    public static byte[] Serialize(object value)
    {
        using MemoryStream stream = new();
        using BigEndianBinaryWriter writer = new(stream);
        WriteValue(writer, value, value.GetType());
        return stream.ToArray();
    }

    private static void WriteValue(BigEndianBinaryWriter writer, object? value, Type declaredType)
    {
        if (value == null)
            return;

        Type valueType = value.GetType();

        if (value is LegacyPadding padding)
        {
            for (int i = 0; i < padding.Length; i++)
                writer.Write((byte)0);
            return;
        }

        if (value is LegacyUbiArtPath path)
        {
            WritePath(writer, path);
            return;
        }

        if (value is string text)
        {
            writer.WriteUbiArtString(text);
            return;
        }

        if (valueType.IsEnum)
        {
            WriteValue(writer, Convert.ChangeType(value, Enum.GetUnderlyingType(valueType)), Enum.GetUnderlyingType(valueType));
            return;
        }

        switch (Type.GetTypeCode(valueType))
        {
            case TypeCode.Byte:
                writer.Write((byte)value);
                return;
            case TypeCode.SByte:
                writer.Write((sbyte)value);
                return;
            case TypeCode.Int16:
                writer.Write((short)value);
                return;
            case TypeCode.UInt16:
                writer.Write((ushort)value);
                return;
            case TypeCode.Int32:
                writer.Write((int)value);
                return;
            case TypeCode.UInt32:
                writer.Write((uint)value);
                return;
            case TypeCode.Int64:
                writer.Write((long)value);
                return;
            case TypeCode.UInt64:
                writer.Write((ulong)value);
                return;
            case TypeCode.Single:
                writer.Write((float)value);
                return;
            case TypeCode.Double:
                writer.Write((double)value);
                return;
            case TypeCode.Boolean:
                writer.Write((bool)value ? 1 : 0);
                return;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
                WriteValue(writer, item, item?.GetType() ?? typeof(object));
            return;
        }

        foreach (MemberInfo member in GetOrderedMembers(valueType))
        {
            object? memberValue = member switch
            {
                PropertyInfo property => property.GetValue(value),
                FieldInfo field => field.GetValue(value),
                _ => null
            };

            Type memberType = member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => typeof(object)
            };

            WriteValue(writer, memberValue, memberType);
        }
    }

    private static IReadOnlyList<MemberInfo> GetOrderedMembers(Type type)
    {
        List<MemberInfo> members = [];
        HashSet<string> seenMemberNames = new(StringComparer.Ordinal);

        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (PropertyInfo property in current.GetProperties(flags)
                .Where(property => property.GetCustomAttribute<LegacyBinaryFieldAttribute>() != null && property.GetIndexParameters().Length == 0))
            {
                if (seenMemberNames.Add(property.Name))
                    members.Add(property);
            }

            foreach (FieldInfo field in current.GetFields(flags)
                .Where(field => field.GetCustomAttribute<LegacyBinaryFieldAttribute>() != null))
            {
                if (seenMemberNames.Add(field.Name))
                    members.Add(field);
            }
        }

        return [.. members
            .OrderBy(member => member.GetCustomAttribute<LegacyBinaryFieldAttribute>()!.Order)
            .ThenBy(member => member.MetadataToken)];
    }

    private static void WritePath(BigEndianBinaryWriter writer, LegacyUbiArtPath path)
    {
        writer.WriteUbiArtString(path.FileName);
        writer.WriteUbiArtString(path.Folder);

        uint resourceId = path.ResourceId ?? Crc32.HashToUInt32(Encoding.UTF8.GetBytes(path.FileName));
        writer.Write(resourceId);
    }
}
