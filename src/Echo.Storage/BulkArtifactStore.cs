using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;

namespace Echo.Storage;

[Export(typeof(IBulkArtifactStore)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class BulkArtifactStore : IBulkArtifactStore
{
    [ImportingConstructor]
    public BulkArtifactStore()
    {
    }

    public bool CanStore<T>(T value) => value is DepthMap or FusedVolume or Surface or TextureCoordinates or SemanticMap;

    public BulkDataSession OpenSession(long memoryBytes, long diskBytes) => new(memoryBytes, diskBytes);

    public bool Supports(Type type) => type == typeof(DepthMap) || type == typeof(FusedVolume)
        || type == typeof(Surface) || type == typeof(TextureCoordinates) || type == typeof(SemanticMap);

    public T Read<T>(string path)
    {
        using var session = OpenSession(8 * 1024 * 1024, 0);
        var value = session.Read<T>(path);
        // In-memory callers own their records. Paged processing uses a scoped
        // session so no record collection outlives its backing storage.
        object result = value switch
        {
            DepthMap map => new DepthMap
            {
                ImageId = map.ImageId,
                Width = map.Width,
                Height = map.Height,
                Depth = Enumerable.Range(0, map.SampleCount).Select(map.DepthAt).ToArray(),
                Confidence = Enumerable.Range(0, map.SampleCount).Select(map.ConfidenceAt).ToArray()
            },
            FusedVolume volume => new FusedVolume
            {
                VoxelSize = volume.VoxelSize,
                InputSamples = volume.InputSamples,
                ConsistentSamples = volume.ConsistentSamples,
                Resolution = volume.Resolution,
                Samples = volume.Samples.ToList()
            },
            SemanticMap semantic => new SemanticMap { ImageId = semantic.ImageId, Width = semantic.Width, Height = semantic.Height, Vegetation = semantic.VegetationSamples.ToArray(), Rock = semantic.RockSamples.ToArray() },
            TextureCoordinates coordinates => new TextureCoordinates { Values = coordinates.Values.ToArray() },
            Surface surface => new Surface
            {
                Vertices = surface.Vertices.ToList(),
                Faces = surface.Faces.ToList(),
                SourceImages = surface.SourceImages.ToList()
            },
            _ => throw new InvalidDataException("Unsupported bulk artifact.")
        };
        return (T)result;
    }

    public void Write<T>(string path, T value, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var header = new BulkArtifactHeader { Storage = "echo-binary-v1" };
        var created = new List<string>();
        var suffix = Guid.NewGuid().ToString("N");
        void Save<TRecord>(string name, IEnumerable<TRecord> values, RecordCodec<TRecord> codec)
        {
            var filename = Path.GetFileNameWithoutExtension(path) + "-" + name + "-" + suffix + ".bin";
            var target = Path.Combine(Path.GetDirectoryName(path)!, filename);
            created.Add(target);
            using var cache = new PageCache(65536, 0);
            using var file = new PageFile(target, cache, true);
            using var records = new PagedList<TRecord>(file, codec);
            foreach (var item in values)
            {
                cancellation.ThrowIfCancellationRequested();
                records.Add(item);
            }
            records.Commit();
            header.Buffers.Add(name, filename);
        }

        var metadata = path + "." + suffix + ".tmp";
        try
        {
            switch (value)
            {
                case DepthMap map:
                    header.Kind = "depth";
                    header.ImageId = map.ImageId;
                    header.Width = map.Width;
                    header.Height = map.Height;
                    Save("depth", Enumerable.Range(0, map.SampleCount).Select(map.DepthAt), new FloatRecordCodec());
                    Save("confidence", Enumerable.Range(0, map.SampleCount).Select(map.ConfidenceAt), new FloatRecordCodec());
                    break;
                case FusedVolume volume:
                    header.Kind = "volume";
                    header.VoxelSize = volume.VoxelSize;
                    header.InputSamples = volume.InputSamples;
                    header.ConsistentSamples = volume.ConsistentSamples;
                    header.Resolution = volume.Resolution;
                    Save("samples", volume.Samples, new VolumeRecordCodec());
                    break;
                case SemanticMap semantic:
                    header.Kind = "semantics";
                    header.ImageId = semantic.ImageId;
                    header.Width = semantic.Width;
                    header.Height = semantic.Height;
                    Save("vegetation", semantic.VegetationSamples, new ByteRecordCodec());
                    Save("rock", semantic.RockSamples, new ByteRecordCodec());
                    break;
                case TextureCoordinates coordinates:
                    header.Kind = "uvs";
                    Save("uvs", coordinates.Values, new UvRecordCodec());
                    break;
                case Surface surface:
                    header.Kind = "surface";
                    Save("vertices", surface.Vertices, new VectorRecordCodec());
                    Save("faces", surface.Faces, new TriangleRecordCodec());
                    var indices = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var image in surface.SourceImages)
                    {
                        if (indices.TryAdd(image, indices.Count))
                        {
                            header.Images.Add(image);
                        }
                    }
                    Save("sources", surface.SourceImages.Select(image => indices[image]), new IntRecordCodec());
                    break;
                default:
                    throw new ArgumentException("Unsupported binary artifact.", nameof(value));
            }
            using (var output = File.Create(metadata))
            {
                JsonSerializer.Serialize(output, header, BulkJsonContext.Default.BulkArtifactHeader);
                output.Flush(true);
            }
            File.Move(metadata, path, false);
            created.Clear();
        }
        finally
        {
            File.Delete(metadata);
            foreach (var file in created)
            {
                File.Delete(file);
            }
        }
    }
}
