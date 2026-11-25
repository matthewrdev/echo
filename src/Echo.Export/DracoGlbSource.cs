using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Echo.Models;

namespace Echo.Export;

internal sealed class DracoGlbSource
{
    private readonly byte[] binary;
    private readonly JsonArray views;
    private readonly JsonArray accessors;

    internal DracoGlbSource(JsonObject root, byte[] binary)
    {
        Root = root;
        this.binary = binary;
        views = root["bufferViews"]!.AsArray();
        accessors = root["accessors"]!.AsArray();
        var meshes = root["meshes"]!.AsArray();
        var primitives = meshes.Count == 1 ? meshes[0]!["primitives"]!.AsArray() : null;
        if (primitives?.Count != 1)
        {
            throw new EchoException("Draco compression expects one mesh primitive in an Echo GLB.", 2);
        }

        Primitive = primitives[0]!.AsObject();
        var attributes = Primitive["attributes"]!.AsObject();
        var supportsMesh = attributes.Count == 2 && (Primitive["mode"]?.GetValue<int>() ?? 4) == 4;
        var supportsImage = root["images"]!.AsArray().Count == 1 && root["buffers"]!.AsArray().Count == 1;
        var supportsAccessors = accessors.Count == 3 && views.Count == 4;
        if (!supportsMesh || !supportsImage || !supportsAccessors || root.ContainsKey("extensionsRequired"))
        {
            throw new EchoException("Draco compression expects an uncompressed, textured Echo GLB.", 2);
        }

        Position = accessors[attributes["POSITION"]!.GetValue<int>()]!.AsObject();
        Uv = accessors[attributes["TEXCOORD_0"]!.GetValue<int>()]!.AsObject();
        Index = accessors[Primitive["indices"]!.GetValue<int>()]!.AsObject();
        Image = root["images"]![0]!.AsObject();
        Positions = ReadFloats(Position, "VEC3", 3);
        Uvs = ReadFloats(Uv, "VEC2", 2);
        Indices = ReadIndices(Index);
        var validCounts = Positions.Length / 3 == Uvs.Length / 2 && Indices.Length % 3 == 0;
        if (!validCounts || Indices.Length == 0)
        {
            throw new EchoException("The GLB has inconsistent mesh accessor counts.", 2);
        }

        DegenerateUvs = CountDegenerateUvs();
    }

    internal JsonObject Root
    {
        get;
    }
    internal JsonObject Primitive
    {
        get;
    }
    internal JsonObject Position
    {
        get;
    }
    internal JsonObject Uv
    {
        get;
    }
    internal JsonObject Index
    {
        get;
    }
    internal JsonObject Image
    {
        get;
    }
    internal float[] Positions
    {
        get;
    }
    internal float[] Uvs
    {
        get;
    }
    internal uint[] Indices
    {
        get;
    }
    internal int DegenerateUvs
    {
        get;
    }

    internal ReadOnlySpan<byte> ImageBytes() => View(Image["bufferView"]!.GetValue<int>());

    private ReadOnlySpan<byte> View(int index)
    {
        var view = views[index]!;
        return binary.AsSpan(view["byteOffset"]?.GetValue<int>() ?? 0, view["byteLength"]!.GetValue<int>());
    }

    private float[] ReadFloats(JsonObject accessor, string type, int components)
    {
        var view = accessor["bufferView"]!.GetValue<int>();
        var packed = views[view]!["byteStride"] == null;
        var supported = accessor["componentType"]!.GetValue<int>() == 5126 && accessor["type"]!.GetValue<string>() == type;
        if (!packed || !supported)
        {
            throw new EchoException("Draco compression expects packed floating-point positions and UVs.", 2);
        }

        var count = checked(accessor["count"]!.GetValue<int>() * components);
        var data = View(view)[(accessor["byteOffset"]?.GetValue<int>() ?? 0)..];
        if (count <= 0 || count > data.Length / sizeof(float))
        {
            throw new EchoException("The GLB has an invalid floating-point accessor length.", 2);
        }

        var values = new float[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(data[(index * 4)..]);
            if (!float.IsFinite(values[index]))
            {
                throw new EchoException("The GLB has a non-finite position or UV.", 2);
            }
        }

        return values;
    }

    private uint[] ReadIndices(JsonObject accessor)
    {
        var data = View(accessor["bufferView"]!.GetValue<int>())[(accessor["byteOffset"]?.GetValue<int>() ?? 0)..];
        var values = new uint[accessor["count"]!.GetValue<int>()];
        var type = accessor["componentType"]!.GetValue<int>();
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = type switch
            {
                5121 => data[index],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(data[(index * 2)..]),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(data[(index * 4)..]),
                _ => throw new EchoException("The GLB uses unsupported triangle indices.", 2)
            };
            if (values[index] >= Positions.Length / 3)
            {
                throw new EchoException("The GLB has an index outside the position accessor.", 2);
            }
        }

        return values;
    }

    private int CountDegenerateUvs()
    {
        var count = 0;
        for (var index = 0; index < Indices.Length; index += 3)
        {
            var a = (int)Indices[index] * 2;
            var b = (int)Indices[index + 1] * 2;
            var c = (int)Indices[index + 2] * 2;
            var area = ((double)Uvs[b] - Uvs[a]) * ((double)Uvs[c + 1] - Uvs[a + 1])
                - ((double)Uvs[b + 1] - Uvs[a + 1]) * ((double)Uvs[c] - Uvs[a]);
            if (area == 0)
            {
                count++;
            }
        }

        return count;
    }
}
