using System.Text.Json;
using System.Buffers;
using System.Buffers.Binary;
using Echo.Contracts;

namespace Echo.Storage;

public sealed class BulkArtifactStore : IBulkArtifactStore
{
    private readonly IArtifactWriteObserver writes;
    private readonly IFeatureArtifactStore features;
    private readonly IMatchArtifactStore matches;

    public BulkArtifactStore(IArtifactWriteObserver writes, IFeatureArtifactStore features, IMatchArtifactStore matches)
    {
        this.writes = writes;
        this.features = features;
        this.matches = matches;
    }

    public bool CanStore<T>(T value) => value is DepthMap or FusedVolume or Surface or TextureCoordinates or SemanticMap or ReconstructionScene or FeatureSet or ImagePair;

    public BulkDataSession OpenSession(long memoryBytes, long diskBytes) => new(memoryBytes, diskBytes);

    public bool Supports(Type type) => type == typeof(DepthMap) || type == typeof(FusedVolume)
        || type == typeof(Surface) || type == typeof(TextureCoordinates) || type == typeof(SemanticMap)
        || type == typeof(ReconstructionScene) || type == typeof(ReconstructionOverview)
        || type == typeof(FeatureSet) || type == typeof(ImagePair);

    public bool SupportsPaging(Type type) => Supports(type) && type != typeof(FeatureSet) && type != typeof(ImagePair);

    public T Read<T>(string path)
    {
        if (typeof(T) == typeof(FeatureSet))
        {
            return (T)(object)features.Read(path);
        }
        if (typeof(T) == typeof(ImagePair))
        {
            return (T)(object)matches.Read(path);
        }
        using var session = OpenSession(8 * 1024 * 1024, 0);
        var value = session.Read<T>(path);
        // In-memory callers own their records. Paged processing uses a scoped
        // session so no record collection outlives its backing storage.
        object result = value switch
        {
            ReconstructionOverview overview => overview,
            ReconstructionScene scene => new ReconstructionScene
            {
                Frame = scene.Frame,
                Origin = scene.Origin,
                Cameras = scene.Cameras,
                Points = scene.Points.ToList(),
                Unplaced = scene.Unplaced,
                Warnings = scene.Warnings,
                Bundle = scene.Bundle
            },
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
            SemanticMap semantic => new SemanticMap { ImageId = semantic.ImageId, Width = semantic.Width, Height = semantic.Height, Vegetation = semantic.VegetationSamples.ToArray(), Rock = semantic.RockSamples.ToArray(), Sky = semantic.SkySamples.ToArray() },
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
        if (value is FeatureSet featureSet)
        {
            features.Write(path, featureSet, cancellation);
            return;
        }
        if (value is ImagePair pair)
        {
            matches.Write(path, pair, cancellation);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var header = new BulkArtifactHeader { Storage = "echo-binary-v1" };
        var created = new List<string>();
        var suffix = Guid.NewGuid().ToString("N");
        void Save<TRecord>(string name, IEnumerable<TRecord> values, RecordCodec<TRecord> codec)
        {
            var filename = Path.GetFileNameWithoutExtension(path) + "-" + name + "-" + suffix + ".bin";
            var target = Path.Combine(Path.GetDirectoryName(path)!, filename);
            created.Add(target);
            WriteRecords(target, values, codec, cancellation);
            header.Buffers.Add(name, filename);
        }

        var metadata = path + "." + suffix + ".tmp";
        try
        {
            switch (value)
            {
                case ReconstructionScene scene:
                    header.Kind = "scene";
                    header.Scene = new ReconstructionOverview
                    {
                        Frame = scene.Frame,
                        Origin = scene.Origin,
                        Cameras = scene.Cameras,
                        Unplaced = scene.Unplaced,
                        Warnings = scene.Warnings,
                        Bundle = scene.Bundle
                    };
                    var photographIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var point in scene.Points)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (photographIds.Add(point.ImageA))
                        {
                            header.Images.Add(point.ImageA);
                        }
                        if (photographIds.Add(point.ImageB))
                        {
                            header.Images.Add(point.ImageB);
                        }
                    }
                    Save("points", scene.Points, new CloudPointRecordCodec(header.Images));
                    break;
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
                    if (semantic.SkySamples.Count > 0)
                    {
                        Save("sky", semantic.SkySamples, new ByteRecordCodec());
                    }
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
            string? metadataHash;
            using (var output = File.Create(metadata))
            using (var hashing = new ChecksummedWriteStream(output))
            {
                JsonSerializer.Serialize(hashing, header, BulkJsonContext.Default.BulkArtifactHeader);
                output.Flush(true);
                metadataHash = hashing.CompleteChecksum();
            }
            File.Move(metadata, path, false);
            if (metadataHash != null)
            {
                writes.Record(path, metadataHash);
            }
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

    private void WriteRecords<TRecord>(string path, IEnumerable<TRecord> values, RecordCodec<TRecord> codec, CancellationToken cancellation)
    {
        if (!values.TryGetNonEnumeratedCount(out var count))
        {
            using var cache = new PageCache(65536, 0);
            using var file = new PageFile(path, cache, true);
            using var records = new PagedList<TRecord>(file, codec);
            var batch = new TRecord[4096];
            var filled = 0;
            foreach (var item in values)
            {
                batch[filled++] = item;
                if (filled == batch.Length)
                {
                    cancellation.ThrowIfCancellationRequested();
                    records.AddRange(batch);
                    filled = 0;
                }
            }
            cancellation.ThrowIfCancellationRequested();
            records.AddRange(batch.AsSpan(0, filled));
            return;
        }

        string? checksum;
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
        using (var hashing = new ChecksummedWriteStream(output))
        {
            Span<byte> header = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(header, 0x31504345);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], 1);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], codec.Size);
            BinaryPrimitives.WriteInt32LittleEndian(header[12..], count);
            hashing.Write(header);
            if (values is PagedList<TRecord> paged && codec.CanCopyFrom(paged.Codec))
            {
                paged.CopyEncodedRecordsTo(hashing, cancellation);
            }
            else
            {
                EncodeRecords(hashing, values, codec, count, cancellation);
            }
            // Preserve the existing page-padded artifact representation exactly.
            var length = 16 + (long)count * codec.Size;
            var padding = (int)((65536 - length % 65536) % 65536);
            var zeros = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                zeros.AsSpan(0, padding).Clear();
                hashing.Write(zeros.AsSpan(0, padding));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(zeros);
            }
            output.Flush(true);
            checksum = hashing.CompleteChecksum();
        }
        if (checksum != null)
        {
            writes.Record(path, checksum);
        }
    }

    private void EncodeRecords<TRecord>(Stream output, IEnumerable<TRecord> values, RecordCodec<TRecord> codec, int expected, CancellationToken cancellation)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(65536, codec.Size));
        try
        {
            var capacity = bytes.Length / codec.Size;
            var count = 0;
            var total = 0;
            foreach (var item in values)
            {
                codec.Encode(item, bytes.AsSpan(count++ * codec.Size, codec.Size));
                total++;
                if (count == capacity)
                {
                    cancellation.ThrowIfCancellationRequested();
                    output.Write(bytes.AsSpan(0, count * codec.Size));
                    count = 0;
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (total != expected)
            {
                throw new InvalidDataException("Artifact record count changed during publication.");
            }
            output.Write(bytes.AsSpan(0, count * codec.Size));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }
}
