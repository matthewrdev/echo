using Echo.Models;
using Echo.Fusion;

namespace Echo.Tests;

internal sealed class FusionTestChunkStore : IFusionChunkStore, IFusionGridStore
{
    private readonly Dictionary<long, VolumeSample[]> saved = [];

    private FusionGrid? grid;
    public int GridBuilds
    {
        get; private set;
    }

    public FusionGrid ReadOrBuild(Func<FusionGrid> build)
    {
        if (grid != null)
        {
            return grid;
        }
        grid = build();
        GridBuilds++;
        return grid;
    }

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
