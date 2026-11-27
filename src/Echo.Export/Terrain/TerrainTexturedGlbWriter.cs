using System.Text.Json;
using Echo.Geometry;

namespace Echo.Export.Terrain;

internal sealed class TerrainTexturedGlbWriter
{
    public void Write(string output, TerrainMeshAsset mesh, string? attribution)
    {
        var views = new List<object>();
        var accessors = new List<object>();
        var primitives = new List<object>();
        var materials = new List<object>();
        var images = new List<object>();
        var textures = new List<object>();
        var writeBinary = new List<Action<BinaryWriter>>();
        var byteOffset = 0;

        var low = new V3(mesh.Positions.Min(point => point.X), mesh.Positions.Min(point => point.Y), mesh.Positions.Min(point => point.Z));
        var high = new V3(mesh.Positions.Max(point => point.X), mesh.Positions.Max(point => point.Y), mesh.Positions.Max(point => point.Z));
        var positions = AddAccessor(mesh.Positions.Count, 5126, "VEC3", checked(mesh.Positions.Count * 12), 34962, writer =>
        {
            foreach (var point in mesh.Positions)
            {
                writer.Write((float)point.X);
                writer.Write((float)point.Y);
                writer.Write((float)point.Z);
            }
        }, [(float)low.X, (float)low.Y, (float)low.Z], [(float)high.X, (float)high.Y, (float)high.Z]);
        var directions = BuildNormals(mesh);
        var normals = AddAccessor(directions.Length, 5126, "VEC3", checked(directions.Length * 12), 34962, writer =>
        {
            foreach (var direction in directions)
            {
                writer.Write((float)direction.X);
                writer.Write((float)direction.Y);
                writer.Write((float)direction.Z);
            }
        });
        var terrainUv = AddUv(mesh.TerrainUvs);
        var wallUv = AddUv(mesh.WallUvs);
        foreach (var surface in mesh.Materials)
        {
            if (surface.Triangles.Length == 0)
            {
                continue;
            }

            var indexView = AddAccessor(surface.Triangles.Length, 5125, "SCALAR", checked(surface.Triangles.Length * 4), 34963, writer =>
            {
                foreach (var index in surface.Triangles)
                {
                    writer.Write((uint)index);
                }
            });
            var material = materials.Count;
            var imageView = AddView(surface.Image.Length, null, writer => writer.Write(surface.Image));
            images.Add(new { name = surface.Name, bufferView = imageView, mimeType = "image/jpeg" });
            textures.Add(new { source = material, sampler = 0 });
            materials.Add(new
            {
                name = surface.Name,
                doubleSided = true,
                pbrMetallicRoughness = new { metallicFactor = 0, roughnessFactor = 1, baseColorTexture = new { index = material, texCoord = surface.TextureCoordinate } }
            });
            var attributes = new Dictionary<string, int> { ["POSITION"] = positions, ["NORMAL"] = normals, ["TEXCOORD_0"] = terrainUv };
            if (surface.TextureCoordinate == 1)
            {
                attributes["TEXCOORD_1"] = wallUv;
            }

            primitives.Add(new { attributes, indices = indexView, material });
        }

        var asset = new Dictionary<string, string> { ["version"] = "2.0", ["generator"] = "Echo terrain stitch" };
        if (!string.IsNullOrWhiteSpace(attribution))
        {
            asset["copyright"] = attribution;
        }

        var root = new
        {
            asset,
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0, name = "Echo stitched terrain" } },
            meshes = new[] { new { primitives } },
            materials,
            textures,
            images,
            samplers = new[] { new { magFilter = 9729, minFilter = 9987, wrapS = 33071, wrapT = 33071 } },
            accessors,
            bufferViews = views,
            buffers = new[] { new { byteLength = byteOffset } }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(root);
        var jsonPadding = Padding(json.Length);
        using var stream = File.Create(output);
        using var binary = new BinaryWriter(stream);
        binary.Write(0x46546c67);
        binary.Write(2);
        binary.Write(checked(28 + json.Length + jsonPadding + byteOffset));
        binary.Write(json.Length + jsonPadding);
        binary.Write(0x4e4f534a);
        binary.Write(json);
        for (var index = 0; index < jsonPadding; index++)
        {
            binary.Write((byte)32);
        }

        binary.Write(byteOffset);
        binary.Write(0x004e4942);
        foreach (var action in writeBinary)
        {
            action(binary);
        }

        int AddView(int length, int? target, Action<BinaryWriter> action)
        {
            var index = views.Count;
            views.Add(target is int usage
                ? (object)new { buffer = 0, byteOffset, byteLength = length, target = usage }
                : new { buffer = 0, byteOffset, byteLength = length });
            var padding = Padding(length);
            writeBinary.Add(writer =>
            {
                action(writer);
                for (var pad = 0; pad < padding; pad++)
                {
                    writer.Write((byte)0);
                }
            });
            byteOffset = checked(byteOffset + length + padding);
            return index;
        }

        int AddAccessor(int count, int componentType, string type, int length, int target, Action<BinaryWriter> action,
            double[]? min = null, double[]? max = null)
        {
            var index = accessors.Count;
            var view = AddView(length, target, action);
            accessors.Add(min == null
                ? (object)new { bufferView = view, componentType, count, type }
                : new { bufferView = view, componentType, count, type, min, max });
            return index;
        }

        int AddUv(IReadOnlyList<double[]> coordinates) =>
            AddAccessor(coordinates.Count, 5126, "VEC2", checked(coordinates.Count * 8), 34962, writer =>
            {
                foreach (var uv in coordinates)
                {
                    writer.Write((float)uv[0]);
                    writer.Write((float)uv[1]);
                }
            });
    }

    private static V3[] BuildNormals(TerrainMeshAsset mesh)
    {
        var normal = new V3[mesh.Positions.Count];
        foreach (var material in mesh.Materials)
        {
            var faces = material.Triangles;
            for (var face = 0; face < faces.Length; face += 3)
            {
                var a = faces[face];
                var b = faces[face + 1];
                var c = faces[face + 2];
                var origin = mesh.Positions[a];
                var areaNormal = (mesh.Positions[b] - origin).Cross(mesh.Positions[c] - origin);
                normal[a] += areaNormal;
                normal[b] += areaNormal;
                normal[c] += areaNormal;
            }
        }

        for (var index = 0; index < normal.Length; index++)
        {
            normal[index] = normal[index].Length > 1e-12 ? normal[index].Unit : new V3(0, 1, 0);
        }

        return normal;
    }

    private static int Padding(int length) => (4 - (length & 3)) & 3;
}
