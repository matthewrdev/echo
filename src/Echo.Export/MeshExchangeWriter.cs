using System.Text.Json.Nodes;
using Echo.Geometry;
using Echo.Models;

namespace Echo.Export;

public sealed class MeshExchangeWriter : IMeshExchangeWriter
{
    public void WriteStl(string path, Surface surface)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]);
        writer.Write((uint)surface.Faces.Count);
        foreach (var face in surface.Faces)
        {
            var first = new V3(surface.Vertices[face[0]]);
            var second = new V3(surface.Vertices[face[1]]);
            var third = new V3(surface.Vertices[face[2]]);
            WriteVector(writer, (second - first).Cross(third - first).Unit);
            WriteVector(writer, first);
            WriteVector(writer, second);
            WriteVector(writer, third);
            writer.Write((ushort)0);
        }
    }

    public void WriteTextured(string directory, MeshExportFormat format, Surface surface, IList<double[]> uvs, string atlas)
    {
        if (uvs.Count != surface.Vertices.Count)
        {
            throw new EchoException("Invalid mesh/UV data.");
        }

        if (format != MeshExportFormat.Usdz)
        {
            File.Copy(atlas, Path.Combine(directory, "atlas.jpg"));
        }

        switch (format)
        {
            case MeshExportFormat.Obj:
                WriteObj(directory, surface, uvs);
                break;
            case MeshExportFormat.Dae:
                using (var writer = File.CreateText(Path.Combine(directory, "cliff.dae")))
                {
                    ColladaMesh.Write(writer, surface, uvs);
                }

                break;
            case MeshExportFormat.Fbx:
                FbxMesh.Write(Path.Combine(directory, "cliff.fbx"), surface, uvs);
                break;
            case MeshExportFormat.Usdz:
                var scene = Path.Combine(directory, "cliff.usda");
                using (var writer = File.CreateText(scene))
                {
                    UsdMesh.Write(writer, surface, uvs);
                }

                UsdzPackage.Write(Path.Combine(directory, "cliff.usdz"), scene, atlas);
                File.Delete(scene);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    public void WriteGltf(string directory, string glb)
    {
        using var input = File.OpenRead(glb);
        using var reader = new BinaryReader(input);
        if (reader.ReadUInt32() != 0x46546c67 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != input.Length)
        {
            throw new EchoException("Invalid GLB header.");
        }

        var jsonLength = reader.ReadInt32();
        if (reader.ReadUInt32() != 0x4e4f534a || jsonLength < 2 || jsonLength > input.Length - input.Position - 8)
        {
            throw new EchoException("Invalid GLB JSON chunk.");
        }

        var document = JsonNode.Parse(reader.ReadBytes(jsonLength))!.AsObject();
        var binaryLength = reader.ReadUInt32();
        if (reader.ReadUInt32() != 0x004e4942 || binaryLength != input.Length - input.Position)
        {
            throw new EchoException("Invalid GLB binary chunk.");
        }

        var image = document["images"]![0]!.AsObject();
        var imageViewIndex = image["bufferView"]!.GetValue<int>();
        var views = document["bufferViews"]!.AsArray();
        var imageView = views[imageViewIndex]!;
        var meshBytes = imageView["byteOffset"]!.GetValue<int>();
        var textureBytes = imageView["byteLength"]!.GetValue<int>();
        var invalidImage = imageViewIndex != views.Count - 1 || meshBytes < 0
            || textureBytes <= 0 || (long)meshBytes + textureBytes > binaryLength;
        if (invalidImage)
        {
            throw new EchoException("Invalid Echo GLB texture layout.");
        }

        CopyBytes(reader, Path.Combine(directory, "cliff.bin"), meshBytes);
        CopyBytes(reader, Path.Combine(directory, "atlas.jpg"), textureBytes);
        views.RemoveAt(imageViewIndex);
        image.Remove("bufferView");
        image["uri"] = "atlas.jpg";
        var buffer = document["buffers"]![0]!;
        buffer["byteLength"] = meshBytes;
        buffer["uri"] = "cliff.bin";
        File.WriteAllText(Path.Combine(directory, "cliff.gltf"), document.ToJsonString());
    }

    private void WriteObj(string directory, Surface surface, IList<double[]> uvs)
    {
        using var writer = File.CreateText(Path.Combine(directory, "cliff.obj"));
        writer.WriteLine("mtllib cliff.mtl");
        foreach (var vertex in surface.Vertices)
        {
            writer.WriteLine(FormattableString.Invariant($"v {vertex[0]:R} {vertex[1]:R} {vertex[2]:R}"));
        }

        foreach (var uv in uvs)
        {
            writer.WriteLine(FormattableString.Invariant($"vt {uv[0]:R} {1 - uv[1]:R}"));
        }

        writer.WriteLine("usemtl Cliff");
        foreach (var face in surface.Faces)
        {
            var first = face[0] + 1;
            var second = face[1] + 1;
            var third = face[2] + 1;
            writer.WriteLine(FormattableString.Invariant($"f {first}/{first} {second}/{second} {third}/{third}"));
        }

        File.WriteAllText(Path.Combine(directory, "cliff.mtl"), "newmtl Cliff\nKd 1 1 1\nmap_Kd atlas.jpg\n");
    }

    private void WriteVector(BinaryWriter writer, V3 value)
    {
        writer.Write((float)value.X);
        writer.Write((float)value.Y);
        writer.Write((float)value.Z);
    }

    private void CopyBytes(BinaryReader reader, string path, int count)
    {
        using var output = File.Create(path);
        Span<byte> buffer = stackalloc byte[8192];
        while (count > 0)
        {
            var block = buffer[..Math.Min(count, buffer.Length)];
            reader.BaseStream.ReadExactly(block);
            output.Write(block);
            count -= block.Length;
        }
    }
}
