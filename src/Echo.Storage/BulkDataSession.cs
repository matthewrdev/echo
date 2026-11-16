using System.Text.Json;
using Echo.Contracts;

namespace Echo.Storage;

public sealed class BulkDataSession : IDisposable
{
    private readonly List<IDisposable> resources = [];
    private readonly PageCache cache;

    public BulkDataSession(long memoryBytes, long diskBytes)
    {
        cache = new PageCache(memoryBytes, diskBytes);
    }

    public PagingStatistics Statistics => cache.Statistics;

    public PagedList<T> Open<T>(string path, RecordCodec<T> codec, bool writable = false, long offset = -1, int count = 0)
    {
        var file = new PageFile(path, cache, writable);
        try
        {
            var list = new PagedList<T>(file, codec, offset, count);
            resources.Add(list);
            return list;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public T Read<T>(string path)
    {
        using var stream = File.OpenRead(path);
        var header = JsonSerializer.Deserialize(stream, BulkJsonContext.Default.BulkArtifactHeader)
            ?? throw new InvalidDataException("Missing binary artifact metadata.");
        if (header.Storage != "echo-binary-v1")
        {
            throw new InvalidDataException("Unsupported artifact storage. Start a new run to regenerate this artifact.");
        }
        string Buffer(string name)
        {
            var file = header.Buffers[name];
            if (string.IsNullOrWhiteSpace(file) || Path.GetFileName(file) != file)
            {
                throw new InvalidDataException("Invalid binary buffer path.");
            }
            return Path.Combine(Path.GetDirectoryName(path)!, file);
        }

        object result;
        if (typeof(T) == typeof(ReconstructionOverview) && header.Kind == "scene")
        {
            result = header.Scene ?? throw new InvalidDataException("Missing scene metadata.");
        }
        else if (typeof(T) == typeof(ReconstructionScene) && header.Kind == "scene")
        {
            var scene = (header.Scene ?? throw new InvalidDataException("Missing scene metadata.")).ToScene();
            scene.Points = Open(Buffer("points"), new CloudPointRecordCodec(header.Images));
            result = scene;
        }
        else if (typeof(T) == typeof(DepthMap) && header.Kind == "depth")
        {
            var depth = Open(Buffer("depth"), new FloatRecordCodec());
            var confidence = Open(Buffer("confidence"), new FloatRecordCodec());
            var valid = header.Width > 0 && header.Height > 0
                && (long)header.Width * header.Height == depth.Count;
            if (!valid || confidence.Count != depth.Count)
            {
                throw new InvalidDataException("Invalid binary depth dimensions.");
            }
            result = new DepthMap
            {
                ImageId = header.ImageId,
                Width = header.Width,
                Height = header.Height,
                Samples = new PagedDepthSamples(depth, confidence)
            };
        }
        else if (typeof(T) == typeof(FusedVolume) && header.Kind == "volume")
        {
            result = new FusedVolume
            {
                VoxelSize = header.VoxelSize,
                InputSamples = header.InputSamples,
                ConsistentSamples = header.ConsistentSamples,
                Resolution = header.Resolution,
                Samples = Open(Buffer("samples"), new VolumeRecordCodec())
            };
        }
        else if (typeof(T) == typeof(SemanticMap) && header.Kind == "semantics")
        {
            var vegetation = Open(Buffer("vegetation"), new ByteRecordCodec());
            var rock = Open(Buffer("rock"), new ByteRecordCodec());
            var valid = header.Width > 0 && header.Height > 0 && (long)header.Width * header.Height == vegetation.Count;
            if (!valid || rock.Count != vegetation.Count)
            {
                throw new InvalidDataException("Invalid semantic map dimensions.");
            }
            result = new SemanticMap { ImageId = header.ImageId, Width = header.Width, Height = header.Height, VegetationSamples = vegetation, RockSamples = rock };
        }
        else if (typeof(T) == typeof(TextureCoordinates) && header.Kind == "uvs")
        {
            result = new TextureCoordinates { Values = Open(Buffer("uvs"), new UvRecordCodec()) };
        }
        else if (typeof(T) == typeof(Surface) && header.Kind == "surface")
        {
            var sources = Open(Buffer("sources"), new StringTableCodec(header.Images));
            var surface = new Surface
            {
                Vertices = Open(Buffer("vertices"), new VectorRecordCodec()),
                Faces = Open(Buffer("faces"), new TriangleRecordCodec()),
                SourceImages = sources
            };
            if (sources.Count != 0 && sources.Count != surface.Faces.Count)
            {
                throw new InvalidDataException("Photograph assignments must match the surface face count.");
            }
            result = surface;
        }
        else
        {
            throw new InvalidDataException("Binary artifact type does not match the requested contract.");
        }
        return (T)result;
    }

    public void Dispose()
    {
        List<Exception>? failures = null;
        foreach (var resource in resources)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }
        resources.Clear();
        try
        {
            cache.Dispose();
        }
        catch (Exception error)
        {
            (failures ??= []).Add(error);
        }
        if (failures != null)
        {
            throw new AggregateException("Failed to flush paging buffers.", failures);
        }
    }
}
