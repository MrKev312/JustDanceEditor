using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

using KevInc.UbiArt.Cinematics.Core;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal static class CinematicMesh3DReader
{
    private const int MaxVectorCount = 100_000;
    private const int MaxElementCount = 1024;

    public static RenderGeometry Read(byte[] bytes)
    {
        CinematicBinaryReader reader = new(bytes);
        int version = reader.ReadInt32();
        if (version is < 0 or > 64)
            throw new InvalidDataException($"Unsupported legacy Mesh3D version {version}.");

        reader.SkipUInt32(); // Archive link.
        Vector3[] vertices = ReadVector3List(reader, "vertex");
        ReadVector3List(reader, "normal");
        Vector2[] uvs = ReadVector2List(reader);

        int elementCount = reader.ReadInt32();
        if (elementCount is < 0 or > MaxElementCount)
            throw new InvalidDataException($"Invalid legacy Mesh3D element count {elementCount} at 0x{reader.Offset - 4:X}.");

        List<int> indices = [];
        for (int elementIndex = 0; elementIndex < elementCount; elementIndex++)
        {
            reader.SkipInt32(); // Material ID.
            int triangleCount = reader.ReadInt32();
            if (triangleCount is < 0 or > MaxVectorCount)
                throw new InvalidDataException($"Invalid legacy Mesh3D triangle count {triangleCount} at 0x{reader.Offset - 4:X}.");

            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                reader.Skip(12); // Vertex indices.
                reader.Skip(12); // Normal indices.
                reader.Skip(12); // UV indices.
                indices.Add(checked((int)reader.ReadUInt32()));
                indices.Add(checked((int)reader.ReadUInt32()));
                indices.Add(checked((int)reader.ReadUInt32()));
            }
        }

        RenderVertex[] renderVertices = ReadUniqueVertexBuffer(reader, vertices, uvs);
        if (renderVertices.Length == 0 || indices.Count == 0)
            throw new InvalidDataException("Legacy Mesh3D did not contain renderable triangles.");

        int maxIndex = renderVertices.Length - 1;
        foreach (int index in indices)
        {
            if ((uint)index > (uint)maxIndex)
                throw new InvalidDataException($"Legacy Mesh3D triangle references missing vertex-buffer index {index}.");
        }

        return RenderGeometry.FromMesh(renderVertices, indices, CinematicGeometrySource.Mesh3D);
    }

    private static Vector3[] ReadVector3List(CinematicBinaryReader reader, string label)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > MaxVectorCount)
            throw new InvalidDataException($"Invalid legacy Mesh3D {label} count {count} at 0x{reader.Offset - 4:X}.");

        Vector3[] values = new Vector3[count];
        for (int i = 0; i < count; i++)
            values[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        return values;
    }

    private static Vector2[] ReadVector2List(CinematicBinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > MaxVectorCount)
            throw new InvalidDataException($"Invalid legacy Mesh3D UV count {count} at 0x{reader.Offset - 4:X}.");

        Vector2[] values = new Vector2[count];
        for (int i = 0; i < count; i++)
            values[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());

        return values;
    }

    private static RenderVertex[] ReadUniqueVertexBuffer(
        CinematicBinaryReader reader,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<Vector2> uvs)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > MaxVectorCount)
            throw new InvalidDataException($"Invalid legacy Mesh3D unique vertex count {count} at 0x{reader.Offset - 4:X}.");

        RenderVertex[] renderVertices = new RenderVertex[count];
        for (int i = 0; i < count; i++)
        {
            int vertexIndex = checked((int)reader.ReadUInt32());
            int uvIndex = checked((int)reader.ReadUInt32());
            if ((uint)vertexIndex >= (uint)vertices.Count)
                throw new InvalidDataException($"Legacy Mesh3D unique vertex references missing vertex {vertexIndex}.");

            if ((uint)uvIndex >= (uint)uvs.Count)
                throw new InvalidDataException($"Legacy Mesh3D unique vertex references missing UV {uvIndex}.");

            Vector3 position = vertices[vertexIndex];
            Vector2 uv = uvs[uvIndex];
            renderVertices[i] = new RenderVertex(position.X, position.Y, position.Z, uv.X, uv.Y);
        }

        return renderVertices;
    }
}
