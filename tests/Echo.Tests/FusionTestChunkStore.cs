using Echo.Contracts;
using Echo.Fusion;

namespace Echo.Tests;

internal sealed class FusionTestChunkStore : IFusionChunkStore
{
    private readonly Dictionary<long, VolumeSample[]> saved = [];

    public int Builds
    {
        get; private set;
    }

    public IEnumerable<VolumeSample> ReadOrBuild(long first, int count, Func<IList<VolumeSample>> build)
    {
        if (saved.TryGetValue(first, out var existing))
        {
            return existing;
        }
        var samples = build().ToArray();
        Builds++;
        saved.Add(first, samples);
        return samples;
    }
}
