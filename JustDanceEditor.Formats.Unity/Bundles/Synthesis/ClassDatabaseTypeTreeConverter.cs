using AssetsTools.NET;
using AssetsTools.NET.Extra;

using System.Text;

namespace JustDanceEditor.Formats.Unity.Bundles.Synthesis;

/// <summary>
/// Converts class database definitions into type trees while working around the missing
/// <see cref="TypeTreeType.TypeBlob"/> initialization in AssetsTools.NET 3.0.5.
/// </summary>
/// <remarks>
/// This mirrors the upstream converter with the fix from nesrak1/AssetsTools.NET@c6b77567b3.
/// It can be removed after that fix is included in a NuGet release.
/// </remarks>
internal sealed class ClassDatabaseTypeTreeConverter
{
    private readonly ClassDatabaseFile classDatabase;
    private readonly Dictionary<string, uint> stringTableLookup = [];
    private readonly Dictionary<string, uint> commonStringTableLookup = [];
    private readonly List<TypeTreeNode> typeTreeNodes = [];
    private uint stringTablePosition;

    private ClassDatabaseTypeTreeConverter(ClassDatabaseFile classDatabase)
    {
        this.classDatabase = classDatabase;
        InitializeDefaultStringTableIndices();
    }

    public static TypeTreeType Convert(ClassDatabaseFile classDatabase, int typeId, bool preferEditor = false)
    {
        ClassDatabaseType type = classDatabase.FindAssetClassByID(typeId)
                                 ?? throw new InvalidOperationException($"Unity class database does not contain type id {typeId}.");
        return new ClassDatabaseTypeTreeConverter(classDatabase).Convert(type, preferEditor);
    }

    private TypeTreeType Convert(ClassDatabaseType type, bool preferEditor)
    {
        TypeTreeType typeTreeType = new()
        {
            TypeId = type.ClassId,
            ScriptTypeIndex = 0xffff,
            IsStrippedType = false,
            ScriptIdHash = Hash128.NewBlankHash(),
            TypeHash = Hash128.NewBlankHash(),
            TypeDependencies = [],
            TypeBlobIsDefinition = true,
            TypeBlob = new TypeTreeBlob()
        };

        ConvertFields(type.GetPreferredNode(preferEditor), 0);

        StringBuilder stringTableBuilder = new();
        foreach (KeyValuePair<string, uint> entry in stringTableLookup.OrderBy(entry => entry.Value))
            stringTableBuilder.Append(entry.Key).Append('\0');

        typeTreeType.StringBuffer = stringTableBuilder.ToString();
        typeTreeType.Nodes = typeTreeNodes;
        typeTreeType.TypeHash = ComputeHash(typeTreeType);
        return typeTreeType;
    }

    private void InitializeDefaultStringTableIndices()
    {
        uint commonStringTablePosition = 0;
        foreach (ushort entry in classDatabase.CommonStringBufferIndices)
        {
            string value = classDatabase.StringTable.GetString(entry);
            if (value.Length == 0)
                continue;

            commonStringTableLookup.Add(value, commonStringTablePosition);
            commonStringTablePosition += (uint)value.Length + 1;
        }
    }

    private void ConvertFields(ClassDatabaseTypeNode node, int depth)
    {
        string fieldName = classDatabase.GetString(node.FieldName);
        string typeName = classDatabase.GetString(node.TypeName);

        typeTreeNodes.Add(new TypeTreeNode
        {
            Level = (byte)depth,
            MetaFlags = node.MetaFlag,
            Index = (uint)typeTreeNodes.Count,
            TypeFlags = (TypeTreeNodeFlags)node.TypeFlags,
            NameStrOffset = GetStringOffset(fieldName),
            ByteSize = node.ByteSize,
            TypeStrOffset = GetStringOffset(typeName),
            Version = node.Version
        });

        foreach (ClassDatabaseTypeNode child in node.Children)
            ConvertFields(child, depth + 1);
    }

    private uint GetStringOffset(string value)
    {
        if (stringTableLookup.TryGetValue(value, out uint localOffset))
            return localOffset;

        if (commonStringTableLookup.TryGetValue(value, out uint commonOffset))
            return commonOffset + 0x80000000;

        uint offset = stringTablePosition;
        stringTableLookup.Add(value, offset);
        stringTablePosition += (uint)value.Length + 1;
        return offset;
    }

    private static Hash128 ComputeHash(TypeTreeType typeTree)
    {
        MD4 md4 = new();
        foreach (TypeTreeNode node in typeTree.Nodes)
        {
            md4.Update(Encoding.UTF8.GetBytes(node.GetTypeString(typeTree.StringBufferBytes)));
            md4.Update(Encoding.UTF8.GetBytes(node.GetNameString(typeTree.StringBufferBytes)));
            md4.Update(BitConverter.GetBytes(node.ByteSize));
            md4.Update(BitConverter.GetBytes(System.Convert.ToInt32(node.TypeFlags)));
            md4.Update(BitConverter.GetBytes(System.Convert.ToInt32(node.Version)));
            md4.Update(BitConverter.GetBytes(System.Convert.ToInt32(node.MetaFlags & 0x4000)));
        }

        return new Hash128(md4.Digest());
    }
}
