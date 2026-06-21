using System.Collections;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal static class LegacyBinarySerializer
{
    private static readonly ConcurrentDictionary<Type, uint> TypeIdCache = new();
    private static readonly object SkippedMemberValue = new();

    public static byte[] Serialize(object value)
    {
        using MemoryStream stream = new();
        Serialize(stream, value);
        return stream.ToArray();
    }

    public static void Serialize(Stream stream, object value)
    {
        using BigEndianBinaryWriter writer = new(stream);
        WriteValue(writer, value, value.GetType(), null);
    }

    public static T Deserialize<T>(Stream stream) where T : notnull
        => Deserialize<T>(stream, LegacyBinarySerializerContext.None);

    public static T Deserialize<T>(Stream stream, LegacyBinarySerializerContext context) where T : notnull
    {
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return Deserialize<T>(memory.ToArray(), context);
    }

    public static object Deserialize(Type type, Stream stream, LegacyBinarySerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(type);

        using MemoryStream memory = new();
        stream.CopyTo(memory);
        LegacyBinaryBufferReader reader = new(memory.ToArray());
        return ReadValue(reader, type, null, null, context)
            ?? throw new InvalidDataException($"Legacy binary deserialization produced null for '{type.FullName}'.");
    }

    public static T Deserialize<T>(byte[] bytes) where T : notnull =>
        Deserialize<T>(bytes, LegacyBinarySerializerContext.None);

    public static T Deserialize<T>(byte[] bytes, LegacyBinarySerializerContext context) where T : notnull =>
        Deserialize<T>(bytes, 0, context, out _);

    public static T Deserialize<T>(byte[] bytes, int offset, out int bytesRead) where T : notnull
        => Deserialize<T>(bytes, offset, LegacyBinarySerializerContext.None, out bytesRead);

    public static T Deserialize<T>(
        byte[] bytes,
        int offset,
        LegacyBinarySerializerContext context,
        out int bytesRead) where T : notnull
    {
        LegacyBinaryBufferReader reader = new(bytes, offset);
        T value = Deserialize<T>(reader, context);
        bytesRead = reader.Offset - offset;
        return value;
    }

    public static T Deserialize<T>(ILegacyBinaryReader reader) where T : notnull =>
        Deserialize<T>(reader, LegacyBinarySerializerContext.None);

    public static T Deserialize<T>(ILegacyBinaryReader reader, LegacyBinarySerializerContext context) where T : notnull =>
        (T)ReadValue(reader, typeof(T), null, null, context)!;

    public static TBase DeserializeTyped<TBase>(ILegacyBinaryReader reader) where TBase : notnull
        => DeserializeTyped<TBase>(reader, LegacyBinarySerializerContext.None);

    public static TBase DeserializeTyped<TBase>(ILegacyBinaryReader reader, LegacyBinarySerializerContext context) where TBase : notnull
    {
        uint typeId = reader.ReadUInt32();
        Type runtimeType = LegacyBinaryTypeRegistry.Resolve(typeof(TBase), typeId, context);
        object value = CreateObject(runtimeType);
        FillObject(reader, value, runtimeType, context);
        return (TBase)value;
    }

    public static uint GetTypeId<T>() => LegacyBinaryTypeIdCache<T>.Value;

    public static bool IsTypeId<T>(uint typeId) => typeId == GetTypeId<T>();

    public static uint GetTypeId(Type type) =>
        TypeIdCache.GetOrAdd(type, ReadTypeIdAttribute);

    private static uint ReadTypeIdAttribute(Type type)
    {
        LegacyBinaryTypeIdAttribute attribute = type.GetCustomAttribute<LegacyBinaryTypeIdAttribute>()
            ?? throw new InvalidOperationException($"Legacy binary type '{type.FullName}' has no type id attribute.");
        return attribute.Value;
    }

    private static class LegacyBinaryTypeIdCache<T>
    {
        public static readonly uint Value = LegacyBinarySerializer.GetTypeId(typeof(T));
    }

    private static object? ReadValue(
        ILegacyBinaryReader reader,
        Type declaredType,
        MemberInfo? member,
        object? owner,
        LegacyBinarySerializerContext context)
    {
        Type valueType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (!ShouldReadMember(member, owner, context))
            return SkippedMemberValue;

        LegacyBinaryPaddingAttribute? paddingAttribute = member?.GetCustomAttribute<LegacyBinaryPaddingAttribute>();
        if (paddingAttribute != null)
        {
            reader.Skip(paddingAttribute.Length);
            return new LegacyPadding(paddingAttribute.Length);
        }

        LegacyBinaryByteCountAttribute? byteCountAttribute = member?.GetCustomAttribute<LegacyBinaryByteCountAttribute>();
        if (byteCountAttribute != null)
        {
            int length = checked(GetIntMemberValue(owner, byteCountAttribute.MemberName) + byteCountAttribute.Add);
            if (valueType != typeof(byte[]))
                throw new InvalidOperationException($"{nameof(LegacyBinaryByteCountAttribute)} can only be used with byte[] members.");

            return reader.ReadBytes(length);
        }

        LegacyBinarySwitchAttribute? switchAttribute = member?.GetCustomAttribute<LegacyBinarySwitchAttribute>();
        if (switchAttribute != null)
            return ReadSwitchedObject(reader, valueType, owner, switchAttribute, context);

        if (valueType == typeof(LegacyPadding))
        {
            throw new InvalidOperationException(
                $"LegacyPadding member '{member?.Name ?? "<root>"}' must declare {nameof(LegacyBinaryPaddingAttribute)} when deserializing.");
        }

        if (valueType == typeof(LegacyUbiArtPath))
            return ReadPath(reader);

        if (valueType == typeof(LegacyUbiArtFolderFirstPath))
            return ReadFolderFirstPath(reader);

        if (valueType == typeof(LegacyUbiArtFlexiblePath))
            return ReadFlexiblePath(reader);

        if (valueType == typeof(string))
            return reader.ReadString();

        if (valueType.IsEnum)
            return Enum.ToObject(valueType, reader.ReadInt32());

        switch (Type.GetTypeCode(valueType))
        {
            case TypeCode.Byte:
                return reader.ReadByte();
            case TypeCode.SByte:
                return unchecked((sbyte)reader.ReadByte());
            case TypeCode.Int16:
                return reader.ReadInt16();
            case TypeCode.UInt16:
                return reader.ReadUInt16();
            case TypeCode.Int32:
                return reader.ReadInt32();
            case TypeCode.UInt32:
                return reader.ReadUInt32();
            case TypeCode.Int64:
                return reader.ReadInt64();
            case TypeCode.UInt64:
                return reader.ReadUInt64();
            case TypeCode.Single:
                return reader.ReadSingle();
            case TypeCode.Double:
                return reader.ReadDouble();
            case TypeCode.Boolean:
                return reader.ReadInt32() != 0;
        }

        if (valueType.IsArray && valueType != typeof(byte[]) && TryGetListElementType(valueType, out Type? arrayElementType) && arrayElementType != null)
            return ReadCountPrefixedArray(reader, valueType, arrayElementType, context);

        if (valueType.IsAbstract || valueType.IsInterface)
            return ReadTypedObject(reader, valueType, context);

        if (TryReadSerializedTypeId(reader, valueType, out object? serializedTypeId))
            return serializedTypeId;

        ValidateConcreteTypeId(reader, valueType);

        object value = CreateObject(valueType);
        FillObject(reader, value, valueType, context);
        return value;
    }

    private static object ReadTypedObject(ILegacyBinaryReader reader, Type declaredType, LegacyBinarySerializerContext context)
    {
        uint typeId = reader.ReadUInt32();
        Type runtimeType = LegacyBinaryTypeRegistry.Resolve(declaredType, typeId, context);
        object value = CreateObject(runtimeType);
        FillObject(reader, value, runtimeType, context);
        return value;
    }

    private static void FillObject(ILegacyBinaryReader reader, object value, Type valueType, LegacyBinarySerializerContext context)
    {
        foreach (MemberInfo member in GetOrderedMembers(valueType, forWrite: false))
        {
            object? memberValue = ReadValue(reader, GetMemberType(member), member, value, context);
            if (ReferenceEquals(memberValue, SkippedMemberValue))
                continue;

            SetMemberValue(member, value, memberValue);
        }
    }

    private static object ReadCountPrefixedArray(
        ILegacyBinaryReader reader,
        Type type,
        Type elementType,
        LegacyBinarySerializerContext context)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > 1_000_000)
            throw new InvalidDataException($"Invalid legacy array length {count} at 0x{reader.Offset - 4:X}.");

        IList list = CreateList(elementType, count);
        for (int i = 0; i < count; i++)
            list.Add(ReadValue(reader, elementType, null, null, context));

        Array array = Array.CreateInstance(elementType, count);
        list.CopyTo(array, 0);
        return array;
    }

    private static void WriteValue(BigEndianBinaryWriter writer, object? value, Type declaredType, MemberInfo? member)
    {
        if (value == null)
            return;

        Type valueType = value.GetType();

        if (TryWriteSerializedTypeId(writer, valueType))
            return;

        LegacyBinaryTypeIdAttribute? typeId = valueType.GetCustomAttribute<LegacyBinaryTypeIdAttribute>();
        if (typeId != null)
            writer.Write(typeId.Value);

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

        if (value is LegacyUbiArtFolderFirstPath folderFirstPath)
        {
            WriteFolderFirstPath(writer, folderFirstPath);
            return;
        }

        if (value is LegacyUbiArtFlexiblePath flexiblePath)
        {
            WriteFlexiblePath(writer, flexiblePath);
            return;
        }

        if (value is string text)
        {
            writer.WriteUbiArtString(text);
            return;
        }

        if (valueType.IsEnum)
        {
            WriteValue(writer, Convert.ChangeType(value, Enum.GetUnderlyingType(valueType)), Enum.GetUnderlyingType(valueType), member);
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
            object?[] items = [.. enumerable.Cast<object?>()];
            if (ShouldWriteCountPrefix(valueType))
                writer.Write(items.Length);

            foreach (object? item in items)
                WriteValue(writer, item, item?.GetType() ?? typeof(object), null);

            return;
        }

        foreach (MemberInfo childMember in GetOrderedMembers(valueType, forWrite: true))
        {
            object? memberValue = childMember switch
            {
                PropertyInfo property => property.GetValue(value),
                FieldInfo field => field.GetValue(value),
                _ => null
            };

            WriteValue(writer, memberValue, GetMemberType(childMember), childMember);
        }
    }

    private static bool ShouldWriteCountPrefix(Type valueType) =>
        valueType.IsArray && valueType != typeof(byte[]);

    private static IReadOnlyList<MemberInfo> GetOrderedMembers(Type type, bool forWrite)
    {
        List<MemberInfo> members = [];
        HashSet<string> seenMemberNames = new(StringComparer.Ordinal);
        Stack<Type> hierarchy = new();

        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            hierarchy.Push(current);

        while (hierarchy.Count > 0)
        {
            Type current = hierarchy.Pop();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

            foreach (PropertyInfo property in current.GetProperties(flags)
                .Where(property => ShouldSerializeProperty(property, forWrite))
                .OrderBy(property => property.MetadataToken))
            {
                if (seenMemberNames.Add(property.Name))
                    members.Add(property);
            }
        }

        return members;
    }

    private static bool ShouldSerializeProperty(PropertyInfo property, bool forWrite) =>
        property.GetIndexParameters().Length == 0 &&
        (forWrite || property.SetMethod != null) &&
        property.GetCustomAttribute<BinarySerializerIgnoreAttribute>() == null;

    private static bool TryReadSerializedTypeId(ILegacyBinaryReader reader, Type valueType, out object? value)
    {
        value = null;
        if (!valueType.IsGenericType || valueType.GetGenericTypeDefinition() != typeof(LegacyBinaryTypeId<>))
            return false;

        Type markerType = valueType.GetGenericArguments()[0];
        uint expectedTypeId = GetTypeId(markerType);
        uint actualTypeId = reader.ReadUInt32();
        if (actualTypeId != expectedTypeId)
        {
            throw new InvalidDataException(
                $"Expected legacy binary type id 0x{expectedTypeId:X8} for {markerType.Name}, got 0x{actualTypeId:X8} at 0x{reader.Offset - 4:X}.");
        }

        value = Activator.CreateInstance(valueType);
        return true;
    }

    private static bool TryWriteSerializedTypeId(BigEndianBinaryWriter writer, Type valueType)
    {
        if (!valueType.IsGenericType || valueType.GetGenericTypeDefinition() != typeof(LegacyBinaryTypeId<>))
            return false;

        writer.Write(GetTypeId(valueType.GetGenericArguments()[0]));
        return true;
    }

    private static void ValidateConcreteTypeId(ILegacyBinaryReader reader, Type valueType)
    {
        LegacyBinaryTypeIdAttribute? typeId = valueType.GetCustomAttribute<LegacyBinaryTypeIdAttribute>();
        if (typeId == null)
            return;

        uint actualTypeId = reader.ReadUInt32();
        if (actualTypeId != typeId.Value)
        {
            throw new InvalidDataException(
                $"Expected legacy binary type id 0x{typeId.Value:X8} for {valueType.Name}, got 0x{actualTypeId:X8} at 0x{reader.Offset - 4:X}.");
        }
    }

    private static LegacyUbiArtPath ReadPath(ILegacyBinaryReader reader)
    {
        string fileName = reader.ReadString();
        string folder = reader.ReadString();
        uint resourceId = reader.ReadUInt32();
        return new LegacyUbiArtPath(fileName, folder, resourceId);
    }

    private static LegacyUbiArtFolderFirstPath ReadFolderFirstPath(ILegacyBinaryReader reader)
    {
        string folder = reader.ReadString();
        string fileName = reader.ReadString();
        uint resourceId = reader.ReadUInt32();
        return new LegacyUbiArtFolderFirstPath(folder, fileName, resourceId);
    }

    private static LegacyUbiArtFlexiblePath ReadFlexiblePath(ILegacyBinaryReader reader)
    {
        string first = reader.ReadString();
        string second = reader.ReadString();
        uint resourceId = reader.ReadUInt32();
        return new LegacyUbiArtFlexiblePath(first, second, resourceId);
    }

    private static void WritePath(BigEndianBinaryWriter writer, LegacyUbiArtPath path)
    {
        writer.WriteUbiArtString(path.FileName);
        writer.WriteUbiArtString(path.Folder);

        uint resourceId = path.ResourceId ?? Crc32.HashToUInt32(Encoding.UTF8.GetBytes(path.FileName));
        writer.Write(resourceId);
    }

    private static void WriteFolderFirstPath(BigEndianBinaryWriter writer, LegacyUbiArtFolderFirstPath path)
    {
        writer.WriteUbiArtString(path.Folder);
        writer.WriteUbiArtString(path.FileName);

        uint resourceId = path.ResourceId ?? Crc32.HashToUInt32(Encoding.UTF8.GetBytes(path.FileName));
        writer.Write(resourceId);
    }

    private static void WriteFlexiblePath(BigEndianBinaryWriter writer, LegacyUbiArtFlexiblePath path)
    {
        writer.WriteUbiArtString(path.First);
        writer.WriteUbiArtString(path.Second);

        string fileName = Path.GetFileName(path.FullPath);
        uint resourceId = path.ResourceId ?? Crc32.HashToUInt32(Encoding.UTF8.GetBytes(fileName));
        writer.Write(resourceId);
    }

    private static Type GetMemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => typeof(object)
    };

    private static bool ShouldReadMember(MemberInfo? member, object? owner, LegacyBinarySerializerContext context)
    {
        LegacyBinaryEngineVersionConditionAttribute[] engineConditions = member?.GetCustomAttributes<LegacyBinaryEngineVersionConditionAttribute>().ToArray() ?? [];
        foreach (LegacyBinaryEngineVersionConditionAttribute condition in engineConditions)
        {
            if (!condition.Matches(context))
                return false;
        }

        LegacyBinaryConditionAttribute[] conditions = member?.GetCustomAttributes<LegacyBinaryConditionAttribute>().ToArray() ?? [];
        foreach (LegacyBinaryConditionAttribute condition in conditions)
        {
            int actual = GetIntMemberValue(owner, condition.MemberName);
            bool matches = actual == condition.Value;
            if (condition.Invert ? matches : !matches)
                return false;
        }

        return true;
    }

    private static object ReadSwitchedObject(
        ILegacyBinaryReader reader,
        Type declaredType,
        object? owner,
        LegacyBinarySwitchAttribute attribute,
        LegacyBinarySerializerContext context)
    {
        string switchValue = GetStringMemberValue(owner, attribute.MemberName);
        Type runtimeType = typeof(LegacyBinarySerializer).Assembly.GetTypes()
            .Where(declaredType.IsAssignableFrom)
            .SingleOrDefault(type => type.GetCustomAttributes<LegacyBinarySwitchCaseAttribute>()
                .Any(switchCase => string.Equals(switchCase.Value, switchValue, StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidDataException($"No legacy binary switch case registered for '{declaredType.Name}' value '{switchValue}'.");

        object value = CreateObject(runtimeType);
        FillObject(reader, value, runtimeType, context);
        return value;
    }

    private static int GetIntMemberValue(object? owner, string memberName)
    {
        object? value = GetMemberValue(owner, memberName);
        return value switch
        {
            int intValue => intValue,
            uint uintValue => checked((int)uintValue),
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            _ => throw new InvalidOperationException($"Legacy binary member '{memberName}' is not an integer discriminator.")
        };
    }

    private static string GetStringMemberValue(object? owner, string memberName)
    {
        object? value = GetMemberValue(owner, memberName);
        return value as string
            ?? throw new InvalidOperationException($"Legacy binary member '{memberName}' is not a string discriminator.");
    }

    private static object? GetMemberValue(object? owner, string memberName)
    {
        if (owner == null)
            throw new InvalidOperationException($"Legacy binary member '{memberName}' requires an owning object.");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo? property = owner.GetType().GetProperty(memberName, flags);
        if (property != null)
            return property.GetValue(owner);

        FieldInfo? field = owner.GetType().GetField(memberName, flags);
        if (field != null)
            return field.GetValue(owner);

        throw new InvalidOperationException($"Legacy binary member '{memberName}' was not found on '{owner.GetType().FullName}'.");
    }

    private static void SetMemberValue(MemberInfo member, object owner, object? value)
    {
        switch (member)
        {
            case PropertyInfo property:
                property.SetValue(owner, value);
                break;
            case FieldInfo field:
                field.SetValue(owner, value);
                break;
        }
    }

    private static object CreateObject(Type type)
    {
        ConstructorInfo? defaultConstructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (defaultConstructor != null)
            return defaultConstructor.Invoke(null);

        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static bool TryGetListElementType(Type type, out Type? elementType)
    {
        elementType = null;
        if (type.IsArray)
        {
            elementType = type.GetElementType();
            return elementType != null;
        }

        Type? enumerableType = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        elementType = enumerableType?.GetGenericArguments()[0];
        return elementType != null;
    }

    private static IList CreateList(Type elementType, int capacity)
    {
        Type listType = typeof(List<>).MakeGenericType(elementType);
        return (IList)(Activator.CreateInstance(listType, capacity) ?? Activator.CreateInstance(listType)!);
    }
}
