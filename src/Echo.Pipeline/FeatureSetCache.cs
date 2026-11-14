using Echo.Contracts;
using Echo.Infrastructure;

namespace Echo.Pipeline;

public sealed class FeatureSetCache : IDisposable
{
    private readonly IJsonCodec json;
    private readonly long capacityBytes;
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<CachedFeatureSet>> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<CachedFeatureSet> recent = new();
    private long retainedBytes;

    public FeatureSetCache(IJsonCodec json, long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);
        this.json = json;
        this.capacityBytes = capacityBytes;
    }

    // Paths identify immutable, fingerprinted artifacts. Callers must treat the
    // returned DTO and its arrays as read-only; matching owns its output objects.
    // The cache belongs to one stage execution, never to a shared service instance.
    public FeatureSet Read(string path, CancellationToken cancellation)
    {
        lock (gate)
        {
            cancellation.ThrowIfCancellationRequested();
            if (entries.TryGetValue(path, out var node))
            {
                recent.Remove(node);
                recent.AddLast(node);
                return node.Value.Features;
            }

            // Serialize misses to avoid decoding the same feature set concurrently.
            // Workers release this lock before numerical matching starts.
            var features = json.Read<FeatureSet>(path);
            cancellation.ThrowIfCancellationRequested();
            var bytes = EstimateBytes(features);
            if (bytes > capacityBytes)
            {
                return features;
            }

            while (retainedBytes + bytes > capacityBytes && recent.First != null)
            {
                var oldest = recent.First;
                entries.Remove(oldest.Value.Path);
                retainedBytes -= oldest.Value.EstimatedBytes;
                recent.RemoveFirst();
            }
            var entry = new CachedFeatureSet(path, features, bytes);
            entries.Add(path, recent.AddLast(entry));
            retainedBytes += bytes;
            return features;
        }
    }

    private long EstimateBytes(FeatureSet features)
    {
        // Conservative managed-storage accounting, including array/object overhead.
        // Evicted values may remain alive in active workers, whose memory is budgeted separately.
        long bytes = 512 + features.ImageId.Length * 2L;
        bytes += (features.Features.Capacity + (long)features.Lines.Capacity) * IntPtr.Size;
        foreach (var feature in features.Features)
        {
            bytes += 128 + feature.Descriptor.Length * 4L + feature.Kind.Length * 2L;
        }
        foreach (var line in features.Lines)
        {
            bytes += 192 + line.Descriptor.Length * 4L + (line.Start.Length + (long)line.End.Length) * 8;
        }
        return bytes;
    }

    public void Dispose()
    {
        lock (gate)
        {
            entries.Clear();
            recent.Clear();
            retainedBytes = 0;
        }
    }
}
