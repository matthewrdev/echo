using System.Runtime.CompilerServices;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage.Pages;
using Echo.Storage.Records;

namespace Echo.Storage.Resources;

public sealed class WorkBuffers : IDisposable
{
    private readonly BulkDataSession session;
    private readonly string directory;
    private int nextFile;
    private readonly List<IDisposable> resources = [];
    private bool disposed;
    private readonly bool preferResident;
    private readonly bool ownsSession;

    public WorkBuffers(BulkDataSession session, string directory, bool preferResident = false)
    {
        this.session = session;
        this.directory = directory;
        this.preferResident = preferResident;
        Directory.CreateDirectory(directory);
    }

    internal WorkBuffers(PageCache cache)
        : this(new BulkDataSession(cache), Path.Combine(Path.GetTempPath(), "echo-scratch-" + Guid.NewGuid().ToString("N")))
    {
        ownsSession = true;
    }

    public PageMemoryReservation? TryReserveMemory(long bytes) => session.TryReserveMemory(bytes);

    public IList<T> List<T>(RecordCodec<T> codec)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var path = Path.Combine(directory, $"buffer-{nextFile++}.bin");
        if (preferResident && !RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            var resident = new ResidentRecordList<T>(session, path, codec);
            resources.Add(resident);
            return resident;
        }
        var result = session.OpenTemporary(path, codec);
        resources.Add(result);
        return result;
    }

    public WorkBuffers CreateScope(bool preferResident = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var scope = new WorkBuffers(session, Path.Combine(directory, $"scope-{nextFile++}"), preferResident || this.preferResident);
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
        if (ownsSession)
        {
            try
            {
                session.Dispose();
                Directory.Delete(directory, true);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }
        disposed = true;
        if (failures != null)
        {
            throw new AggregateException("Failed to release temporary buffers.", failures);
        }
    }

    public TemporaryRecordSet<T> TemporaryRecords<T>(RecordCodec<T> codec, bool preferResident = false)
        => session.TemporaryRecords(directory, codec, preferResident);

    public IList<T> Filled<T>(int count, T value, RecordCodec<T> codec, CancellationToken cancellation = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        cancellation.ThrowIfCancellationRequested();
        var result = List(codec);
        if (result is PagedList<T> paged)
        {
            paged.AddRepeated(value, count, cancellation);
        }
        else
        {
            for (var index = 0; index < count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                result.Add(value);
            }
        }
        return result;
    }

    public Surface Surface() => new()
    {
        Vertices = List(new VectorRecordCodec()),
        Faces = List(new TriangleRecordCodec()),
        SourceImages = List(new StringTableCodec())
    };
}
