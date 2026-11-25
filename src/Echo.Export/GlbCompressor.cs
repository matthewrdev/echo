using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Infrastructure;
using Echo.Models;
using SkiaSharp;

namespace Echo.Export;

public sealed class GlbCompressor : IGlbCompressor
{
    private const string DracoExtension = "KHR_draco_mesh_compression";
    private readonly IFileOperations files;

    public GlbCompressor(IFileOperations files)
    {
        this.files = files;
    }

    public unsafe int Compress(string source, string destination, int? jpegQuality = null)
    {
        if (jpegQuality is < 1 or > 100)
        {
            throw new EchoException("JPEG quality must be between 1 and 100.", 2);
        }

        var asset = Read(source);
        var triangles = asset.Indices.Length / 3;
        int[] precisions = asset.DegenerateUvs > 0 ? [0] : [12, 16, 22, 0];
        fixed (float* positions = asset.Positions, uvs = asset.Uvs)
        fixed (uint* indices = asset.Indices)
        {
            foreach (var precision in precisions)
            {
                var info = Encode(positions, uvs, indices, asset.Positions.Length / 3, triangles, precision);
                try
                {
                    if (info.triangles != triangles || info.degenerateUvs != asset.DegenerateUvs)
                    {
                        continue;
                    }

                    var geometry = new byte[info.length];
                    Marshal.Copy(info.data, geometry, 0, info.length);
                    var image = EncodeImage(asset, jpegQuality);
                    Write(destination, asset, info, geometry, image);
                    return precision;
                }
                finally
                {
                    DracoNative.Free(info.data);
                }
            }
        }

        throw new EchoException("Draco compression could not preserve the source UV triangle counts.", 5);
    }

    private DracoGlbSource Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        var magic = reader.ReadUInt32();
        var version = reader.ReadUInt32();
        var length = reader.ReadUInt32();
        var validHeader = magic == 0x46546c67 && version == 2 && length == reader.BaseStream.Length;
        if (!validHeader)
        {
            throw new EchoException("Not a valid GLB 2.0 file.", 2);
        }

        var json = ReadChunk(reader, 0x4e4f534a);
        var data = ReadChunk(reader, 0x004e4942);
        return new DracoGlbSource(JsonNode.Parse(json)!.AsObject(), data);
    }

    private byte[] ReadChunk(BinaryReader reader, uint expectedType)
    {
        var size = reader.ReadInt32();
        var type = reader.ReadUInt32();
        var validChunk = type == expectedType && size >= 0 && size <= reader.BaseStream.Length - reader.BaseStream.Position;
        if (!validChunk)
        {
            throw new EchoException("The GLB has an invalid chunk.", 2);
        }

        return reader.ReadBytes(size);
    }

    private unsafe DracoMeshInfo Encode(float* positions, float* uvs, uint* indices, int vertices, int triangles, int precision)
    {
        try
        {
            if (DracoNative.Encode(positions, uvs, indices, vertices, triangles, precision, out var info) != 0)
            {
                throw new EchoException("Echo could not encode and verify the Draco mesh.", 5);
            }

            return info;
        }
        catch (DllNotFoundException)
        {
            throw new EchoException("The Echo Draco codec is unavailable. Use a build that includes the bundled codec.", 5);
        }
    }

    private byte[] EncodeImage(DracoGlbSource asset, int? quality)
    {
        if (quality is null)
        {
            return asset.ImageBytes().ToArray();
        }

        using var image = SKBitmap.Decode(asset.ImageBytes()) ?? throw new EchoException("Could not decode the GLB atlas.", 5);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality.Value)
            ?? throw new EchoException("Could not encode the GLB atlas as JPEG.", 5);
        asset.Image["mimeType"] = "image/jpeg";
        return encoded.ToArray();
    }

    private void Write(string destination, DracoGlbSource asset, DracoMeshInfo info, byte[] geometry, byte[] image)
    {
        var imageOffset = Align(geometry.Length);
        var binaryLength = Align(checked(imageOffset + image.Length));
        var root = asset.Root;
        if (root["extensionsUsed"] is not JsonArray used)
        {
            used = [];
            root["extensionsUsed"] = used;
        }

        used.Add(DracoExtension);
        root["extensionsRequired"] = new JsonArray(JsonValue.Create(DracoExtension));
        root["buffers"]![0]!["byteLength"] = binaryLength;
        root["bufferViews"] = JsonSerializer.SerializeToNode(new[]
        {
            new { buffer = 0, byteOffset = 0, byteLength = geometry.Length },
            new { buffer = 0, byteOffset = imageOffset, byteLength = image.Length }
        });
        asset.Image["bufferView"] = 1;
        var extensions = asset.Primitive["extensions"] as JsonObject;
        if (extensions == null)
        {
            extensions = [];
            asset.Primitive["extensions"] = extensions;
        }

        extensions[DracoExtension] = JsonSerializer.SerializeToNode(new
        {
            bufferView = 0,
            attributes = new Dictionary<string, int>
            {
                ["POSITION"] = info.positionId,
                ["TEXCOORD_0"] = info.uvId
            }
        });
        DescribeAccessor(asset.Position, info.vertices);
        DescribeAccessor(asset.Uv, info.vertices);
        DescribeAccessor(asset.Index, info.triangles * 3);
        asset.Index["componentType"] = info.maximumIndex <= ushort.MaxValue ? 5123 : 5125;
        asset.Position["min"] = JsonSerializer.SerializeToNode(new[] { info.minX, info.minY, info.minZ });
        asset.Position["max"] = JsonSerializer.SerializeToNode(new[] { info.maxX, info.maxY, info.maxZ });

        var json = JsonSerializer.SerializeToUtf8Bytes(root);
        var paddedJson = Align(json.Length);
        var fileLength = checked((uint)(28L + paddedJson + binaryLength));
        files.AtomicWrite(Path.GetFullPath(destination), stream =>
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(0x46546c67u);
            writer.Write(2u);
            writer.Write(fileLength);
            writer.Write(paddedJson);
            writer.Write(0x4e4f534au);
            writer.Write(json);
            WritePadding(writer, paddedJson - json.Length, 32);
            writer.Write(binaryLength);
            writer.Write(0x004e4942u);
            writer.Write(geometry);
            WritePadding(writer, imageOffset - geometry.Length, 0);
            writer.Write(image);
            WritePadding(writer, binaryLength - imageOffset - image.Length, 0);
        });
    }

    private void DescribeAccessor(JsonObject accessor, int count)
    {
        accessor.Remove("byteOffset");
        accessor.Remove("bufferView");
        accessor.Remove("min");
        accessor.Remove("max");
        accessor["count"] = count;
    }

    private void WritePadding(BinaryWriter writer, int bytes, byte value)
    {
        for (var index = 0; index < bytes; index++)
        {
            writer.Write(value);
        }
    }

    private int Align(int bytes) => checked((bytes + 3) & ~3);
}
