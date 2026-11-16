using System.ComponentModel.Composition;
using System.Text;
using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;

namespace Echo.Export;

[Export(typeof(IGlbWriter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class GlbWriter : IGlbWriter
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;

    [ImportingConstructor]
    public GlbWriter(IFileOperations fileOperations, IJsonCodec jsonCodec)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
    }

    public void Write(string output, Surface mesh, IList<double[]> uvs, byte[] texture)
    {
        if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0 || uvs.Count != mesh.Vertices.Count)
        {
            throw new EchoException("Invalid mesh/UV data.");
        }

        var positionsOffset = 0;
        var uvOffset = checked(mesh.Vertices.Count * 12);
        var indexOffset = checked(uvOffset + uvs.Count * 8);
        var imageOffset = checked(indexOffset + mesh.Faces.Count * 12);
        var binaryLength = checked((imageOffset + texture.Length + 3L) / 4 * 4);
        var first = mesh.Vertices[0];
        var minimum = new float[] { (float)first[0], (float)first[1], (float)first[2] };
        var maximum = (float[])minimum.Clone();
        foreach (var vertex in mesh.Vertices)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                var value = (float)vertex[axis];
                minimum[axis] = Math.Min(minimum[axis], value);
                maximum[axis] = Math.Max(maximum[axis], value);
            }
        }
        var document = new GlbDocument
        {
            Asset = new GlbAsset
            {
                Version = "2.0",
                Generator = "Echo 0.1 experimental"
            },
            Scene = 0,
            Scenes = new[]
            {
                new GlbScene
                {
                    Nodes = new[]
                    {
                        0
                    }
                }
            },
            Nodes = new[]
            {
                new GlbNode
                {
                    Mesh = 0,
                    Name = "Echo cliff"
                }
            },
            Meshes = new[]
            {
                new GlbMesh
                {
                    Primitives = new[]
                    {
                        new GlbPrimitive
                        {
                            Attributes = new Dictionary<string, int>
                            {
                                ["POSITION"] = 0,
                                ["TEXCOORD_0"] = 1
                            },
                            Indices = 2,
                            Material = 0
                        }
                    }
                }
            },
            Materials = new[]
            {
                new GlbMaterial
                {
                    DoubleSided = true,
                    PbrMetallicRoughness = new GlbMetallicRoughness
                    {
                        BaseColorTexture = new GlbTextureReference
                        {
                            Index = 0
                        },
                        MetallicFactor = 0,
                        RoughnessFactor = 1
                    }
                }
            },
            Textures = new[]
            {
                new GlbTexture
                {
                    Source = 0,
                    Sampler = 0
                }
            },
            Samplers = new[]
            {
                new GlbSampler
                {
                    MagFilter = 9729,
                    MinFilter = 9987,
                    WrapS = 33071,
                    WrapT = 33071
                }
            },
            Images = new[]
            {
                new GlbImage
                {
                    BufferView = 3,
                    MimeType = "image/jpeg"
                }
            },
            Buffers = new[]
            {
                new GlbBuffer
                {
                    ByteLength = binaryLength
                }
            },
            BufferViews = new object[]
            {
                new GlbTargetBufferView
                {
                    Buffer = 0,
                    ByteOffset = positionsOffset,
                    ByteLength = uvOffset - positionsOffset,
                    Target = 34962
                },
                new GlbTargetBufferView
                {
                    Buffer = 0,
                    ByteOffset = uvOffset,
                    ByteLength = indexOffset - uvOffset,
                    Target = 34962
                },
                new GlbTargetBufferView
                {
                    Buffer = 0,
                    ByteOffset = indexOffset,
                    ByteLength = imageOffset - indexOffset,
                    Target = 34963
                },
                new GlbBufferView
                {
                    Buffer = 0,
                    ByteOffset = imageOffset,
                    ByteLength = texture.Length
                }
            },
            Accessors = new object[]
            {
                new GlbPositionAccessor
                {
                    BufferView = 0,
                    ComponentType = 5126,
                    Count = mesh.Vertices.Count,
                    Type = "VEC3",
                    Min = minimum,
                    Max = maximum
                },
                new GlbAccessor
                {
                    BufferView = 1,
                    ComponentType = 5126,
                    Count = uvs.Count,
                    Type = "VEC2"
                },
                new GlbAccessor
                {
                    BufferView = 2,
                    ComponentType = 5125,
                    Count = mesh.Faces.Count * 3,
                    Type = "SCALAR"
                }
            }
        };
        var json = Encoding.UTF8.GetBytes(jsonCodec.Encode(document));
        var padding = (4 - json.Length % 4) % 4;
        var totalLength = 12L + 8 + json.Length + padding + 8 + binaryLength;
        if (totalLength > uint.MaxValue)
        {
            throw new EchoException("GLB exceeds the format's 4 GB limit.");
        }
        fileOperations.AtomicWrite(output, stream =>
        {
            using var glb = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            glb.Write(0x46546c67u);
            glb.Write(2u);
            glb.Write((uint)totalLength);
            glb.Write((uint)(json.Length + padding));
            glb.Write(0x4e4f534au);
            glb.Write(json);
            for (var i = 0; i < padding; i++)
            {
                glb.Write((byte)32);
            }
            glb.Write((uint)binaryLength);
            glb.Write(0x004e4942u);
            foreach (var vertex in mesh.Vertices)
            {
                glb.Write((float)vertex[0]);
                glb.Write((float)vertex[1]);
                glb.Write((float)vertex[2]);
            }
            foreach (var uv in uvs)
            {
                glb.Write((float)uv[0]);
                glb.Write((float)uv[1]);
            }
            foreach (var face in mesh.Faces)
            {
                glb.Write((uint)face[0]);
                glb.Write((uint)face[1]);
                glb.Write((uint)face[2]);
            }
            glb.Write(texture);
            for (var i = imageOffset + (long)texture.Length; i < binaryLength; i++)
            {
                glb.Write((byte)0);
            }
        });
    }

    private Dimensions ImageDimensions(byte[] data)
    {
        if (data.Length > 24 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71)
        {
            return new Dimensions(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4)));
        }

        if (data.Length < 4 || data[0] != 255 || data[1] != 216)
        {
            return new Dimensions(null, null);
        }

        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset++] != 255)
            {
                break;
            }

            while (offset < data.Length && data[offset] == 255)
            {
                offset++;
            }

            if (offset + 2 >= data.Length)
            {
                break;
            }

            var marker = data[offset++];
            if (marker is 0xd9 or 0xda)
            {
                break;
            }

            var length = (data[offset] << 8) | data[offset + 1];
            if (length < 2 || offset + length > data.Length)
            {
                break;
            }

            if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc) && length >= 7)
            {
                return new Dimensions((data[offset + 5] << 8) | data[offset + 6], (data[offset + 3] << 8) | data[offset + 4]);
            }

            offset += length;
        }

        return new Dimensions(null, null);
    }

    public object Inspect(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x46546c67 || reader.ReadUInt32() != 2)
        {
            throw new EchoException("Not a GLB 2.0 file.");
        }

        var length = reader.ReadUInt32();
        if (length != stream.Length)
        {
            throw new EchoException("GLB length mismatch.");
        }

        var jsonLength = reader.ReadInt32();
        if (reader.ReadUInt32() != 0x4e4f534a || jsonLength < 2 || jsonLength > stream.Length - stream.Position)
        {
            throw new EchoException("Invalid GLB JSON chunk.");
        }

        using var document = JsonDocument.Parse(reader.ReadBytes(jsonLength));
        var root = document.RootElement;
        var binaryOffset = stream.Position + 8;
        if (stream.Position + 8 > stream.Length)
        {
            throw new EchoException("Missing GLB binary chunk.");
        }

        var binaryLength = reader.ReadUInt32();
        var binaryType = reader.ReadUInt32();
        if (binaryType != 0x004e4942 || binaryOffset + binaryLength > stream.Length)
        {
            throw new EchoException("Invalid GLB binary chunk.");
        }

        var textures = new List<object>();
        if (root.TryGetProperty("images", out var imageList))
        {
            foreach (var image in imageList.EnumerateArray())
            {
                if (!image.TryGetProperty("bufferView", out var viewIndex))
                {
                    textures.Add(new GlbExternalTexture
                    {
                        External = true
                    });
                    continue;
                }

                var view = root.GetProperty("bufferViews")[viewIndex.GetInt32()];
                var offset = view.TryGetProperty("byteOffset", out var offsetValue) ? offsetValue.GetInt64() : 0;
                var size = view.GetProperty("byteLength").GetInt32();
                if (offset < 0 || size < 0 || offset + size > binaryLength)
                {
                    throw new EchoException("Image lies outside the GLB binary buffer.");
                }

                stream.Position = binaryOffset + offset;
                var data = reader.ReadBytes(size);
                var dimensions = ImageDimensions(data);
                textures.Add(new GlbEmbeddedTexture
                {
                    MimeType = image.TryGetProperty("mimeType", out var mime) ? mime.GetString() : null,
                    Bytes = size,
                    Width = dimensions.Width,
                    Height = dimensions.Height
                });
            }
        }

        var faces = 0;
        var vertices = 0;
        var bounds = new List<object>();
        foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
        {
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                var position = root.GetProperty("accessors")[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                var count = position.GetProperty("count").GetInt32();
                vertices += count;
                faces += primitive.TryGetProperty("indices", out var index) ? root.GetProperty("accessors")[index.GetInt32()].GetProperty("count").GetInt32() / 3 : count / 3;
                bounds.Add(new GlbBounds
                {
                    Min = position.TryGetProperty("min", out var min) ? min.Clone() : (JsonElement?)null,
                    Max = position.TryGetProperty("max", out var max) ? max.Clone() : (JsonElement?)null
                });
            }
        }

        return new GlbInspectionReport
        {
            SchemaVersion = 1,
            Path = Path.GetFullPath(path),
            Bytes = length,
            Vertices = vertices,
            Faces = faces,
            Bounds = bounds,
            Images = textures.Count,
            Textures = textures,
            Note = "Raw accessor bounds; scene-node transforms and compression must be considered for geometric comparison."
        };
    }
}
