using Echo.Storage;

namespace Echo.Simplification;

// Fixed buckets and compact chained records keep the initial topology index
// within the page-cache budget without issuing a database query per half-edge.
// Records retain first-encounter order, including across hash collisions.
internal sealed class InitialEdgeIndex
{
    private readonly IList<int> buckets;
    private readonly IList<InitialEdgeRecord> records;

    public InitialEdgeIndex(int faceCount, WorkBuffers? work, CancellationToken cancellation)
    {
        var bucketCount = 16;
        while (bucketCount < faceCount && bucketCount < 1 << 26)
        {
            bucketCount *= 2;
        }
        buckets = work?.List(new IntRecordCodec()) ?? new List<int>(bucketCount);
        for (var index = 0; index < bucketCount; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            buckets.Add(-1);
        }
        records = work?.List(new InitialEdgeRecordCodec()) ?? new List<InitialEdgeRecord>();
    }

    public int Count => records.Count;

    public void Add(int from, int to)
    {
        var a = Math.Min(from, to);
        var b = Math.Max(from, to);
        var hash = unchecked((uint)a * 0x9e3779b9u + (uint)b);
        hash ^= hash >> 16;
        hash *= 0x85ebca6bu;
        hash ^= hash >> 13;
        var bucket = (int)(hash & (uint)(buckets.Count - 1));
        var first = buckets[bucket];
        for (var slot = first; slot >= 0;)
        {
            var record = records[slot];
            if (record.A == a && record.B == b)
            {
                return;
            }
            slot = record.Next;
        }
        var next = records.Count;
        records.Add(new InitialEdgeRecord(a, b, first));
        buckets[bucket] = next;
    }

    public IEnumerable<Edge> Edges()
    {
        foreach (var record in records)
        {
            yield return new Edge(record.A, record.B);
        }
    }
}
