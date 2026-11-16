using Echo.Contracts;

namespace Echo.Storage;

public sealed class WorkBuffers
{
    private readonly BulkDataSession session;
    private readonly string directory;
    private int nextFile;

    public WorkBuffers(BulkDataSession session, string directory)
    {
        this.session = session;
        this.directory = directory;
        Directory.CreateDirectory(directory);
    }

    public IList<T> List<T>(RecordCodec<T> codec)
        => session.Open(Path.Combine(directory, $"buffer-{nextFile++}.bin"), codec, true);

    public TemporaryRecordSet<T> TemporaryRecords<T>(RecordCodec<T> codec)
        => session.TemporaryRecords(directory, codec);

    public IList<T> Filled<T>(int count, T value, RecordCodec<T> codec)
    {
        var result = List(codec);
        for (var index = 0; index < count; index++)
        {
            result.Add(value);
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
