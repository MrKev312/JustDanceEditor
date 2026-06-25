using System.Reflection;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal static class LegacyBinaryTypeRegistry
{
    private static readonly Lazy<IReadOnlyDictionary<Type, IReadOnlyDictionary<uint, IReadOnlyList<LegacyBinaryTypeRegistration>>>> TypeMaps = new(BuildTypeMaps);

    public static Type Resolve(Type baseType, uint typeId)
        => Resolve(baseType, typeId, LegacyBinarySerializerContext.None);

    public static Type Resolve(Type baseType, uint typeId, LegacyBinarySerializerContext context)
    {
        if (TryResolve(baseType, typeId, context, out Type? resolved))
            return resolved!;

        throw new InvalidDataException($"No legacy binary type registered for base '{baseType.Name}' and type id 0x{typeId:X8}.");
    }

    public static bool TryResolve(Type baseType, uint typeId, out Type? resolved)
        => TryResolve(baseType, typeId, LegacyBinarySerializerContext.None, out resolved);

    public static bool TryResolve(Type baseType, uint typeId, LegacyBinarySerializerContext context, out Type? resolved)
    {
        resolved = null;
        foreach (KeyValuePair<Type, IReadOnlyDictionary<uint, IReadOnlyList<LegacyBinaryTypeRegistration>>> map in TypeMaps.Value)
        {
            if (!baseType.IsAssignableFrom(map.Key))
                continue;

            if (TryResolveFromMap(baseType, typeId, context, map.Value, out resolved))
                return true;
        }

        if (TypeMaps.Value.TryGetValue(baseType, out IReadOnlyDictionary<uint, IReadOnlyList<LegacyBinaryTypeRegistration>>? directMap))
            return TryResolveFromMap(baseType, typeId, context, directMap, out resolved);

        return false;
    }

    private static bool TryResolveFromMap(
        Type baseType,
        uint typeId,
        LegacyBinarySerializerContext context,
        IReadOnlyDictionary<uint, IReadOnlyList<LegacyBinaryTypeRegistration>> map,
        out Type? resolved)
    {
        resolved = null;
        if (!map.TryGetValue(typeId, out IReadOnlyList<LegacyBinaryTypeRegistration>? candidates))
            return false;

        LegacyBinaryTypeRegistration[] matches = [.. candidates.Where(candidate => candidate.Attribute.Matches(context))];
        if (matches.Length == 1)
        {
            resolved = matches[0].RuntimeType;
            return true;
        }

        if (matches.Length == 0)
            return false;

        string versions = string.Join(", ", matches.Select(match => match.RuntimeType.FullName));
        throw new InvalidDataException(
            $"Legacy binary type id 0x{typeId:X8} for base '{baseType.Name}' is ambiguous for engine version '{context.EngineVersion?.ToString() ?? "<none>"}': {versions}.");
    }

    private static IReadOnlyDictionary<Type, IReadOnlyDictionary<uint, IReadOnlyList<LegacyBinaryTypeRegistration>>> BuildTypeMaps()
    {
        Dictionary<Type, Dictionary<uint, List<LegacyBinaryTypeRegistration>>> maps = [];
        foreach (Type type in typeof(LegacyBinaryTypeRegistry).Assembly.GetTypes())
        {
            LegacyBinaryTypeIdAttribute? attribute = type.GetCustomAttribute<LegacyBinaryTypeIdAttribute>();
            if (attribute == null)
                continue;

            for (Type? current = type.BaseType; current != null && current != typeof(object); current = current.BaseType)
                AddType(maps, current, attribute, type);
        }

        return maps.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyDictionary<uint, IReadOnlyList<LegacyBinaryTypeRegistration>>)entry.Value.ToDictionary(
                inner => inner.Key,
                inner => (IReadOnlyList<LegacyBinaryTypeRegistration>)inner.Value));
    }

    private static void AddType(
        Dictionary<Type, Dictionary<uint, List<LegacyBinaryTypeRegistration>>> maps,
        Type baseType,
        LegacyBinaryTypeIdAttribute attribute,
        Type runtimeType)
    {
        if (!maps.TryGetValue(baseType, out Dictionary<uint, List<LegacyBinaryTypeRegistration>>? map))
        {
            map = [];
            maps[baseType] = map;
        }

        if (!map.TryGetValue(attribute.Value, out List<LegacyBinaryTypeRegistration>? registrations))
        {
            registrations = [];
            map[attribute.Value] = registrations;
        }

        foreach (LegacyBinaryTypeRegistration registration in registrations)
        {
            if (registration.RuntimeType == runtimeType)
                return;

            if (RangesOverlap(registration.Attribute, attribute))
            {
                throw new InvalidOperationException(
                    $"Legacy binary type id 0x{attribute.Value:X8} is registered for overlapping versions on '{registration.RuntimeType.FullName}' and '{runtimeType.FullName}'.");
            }
        }

        registrations.Add(new LegacyBinaryTypeRegistration(runtimeType, attribute));
    }

    private static bool RangesOverlap(LegacyBinaryTypeIdAttribute left, LegacyBinaryTypeIdAttribute right) =>
        left.MinEngineVersion <= right.MaxEngineVersion &&
        right.MinEngineVersion <= left.MaxEngineVersion;

    private sealed record LegacyBinaryTypeRegistration(Type RuntimeType, LegacyBinaryTypeIdAttribute Attribute);
}