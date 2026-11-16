using Echo.Contracts;

namespace Echo.Storage;

public sealed class WorkBuffers : IDisposable
{
    private readonly BulkDataSession session;
    private readonly string directory;
    private int nextFile;
    private readonly List<IDisposable> resources = [];
    private bool disposed;

    public WorkBuffers(BulkDataSession session, string directory)
    {
        this.session = session;
        this.directory = directory;
        Directory.CreateDirectory(directory);
    }

    public IList<T> List<T>(RecordCodec<T> codec)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var result = session.OpenTemporary(Path.Combine(directory, $"buffer-{nextFile++}.bin"), codec);
        resources.Add(result);
        return result;
    }

    public WorkBuffers CreateScope()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var scope = new WorkBuffers(session, Path.Combine(directory, $"scope-{nextFile++}"));
        resources.Add(scope);
        return scope;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
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
        disposed = true;
        if (failures != null)
        {
            throw new AggregateException("Failed to release temporary buffers.", failures);
        }
    }

    public TemporaryRecordSet<T> TemporaryRecords<T>(RecordCodec<T> codec)
        => session.TemporaryRecords(directory, codec);

    public IList<T> Filled<T>(int count, T value, RecordCodec<T> codec, CancellationToken cancellation = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        cancellation.ThrowIfCancellationRequested();
        var result = (PagedList<T>)List(codec);
        result.AddRepeated(value, count, cancellation);
        return result;
    }

    public Surface Surface() => new()
    {
        Vertices = List(new VectorRecordCodec()),
        Faces = List(new TriangleRecordCodec()),
        SourceImages = List(new StringTableCodec())
    };
}
