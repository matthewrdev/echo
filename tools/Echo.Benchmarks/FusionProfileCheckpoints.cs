using Echo.Models;
using Echo.Fusion;

namespace Echo.Benchmarks;

internal sealed class FusionProfileCheckpoints : IFusionChunkStore, IFusionGridStore
{
    private readonly Dictionary<long, VolumeSample[]> chunks = [];
    private FusionGrid? grid;
    public int Builds
    {
        get; private set;
    }
    public int GridBuilds
    {
        get; private set;
    }

    public IEnumerable<VolumeSample> ReadOrBuild(long first, int count, Func<IList<VolumeSample>> build)
    {
        if (chunks.TryGetValue(first, out var saved))
        {
            return saved;
        }

        var result = build().ToArray();
        chunks.Add(first, result);
        Builds++;
        return result;
    }

    public FusionGrid ReadOrBuild(Func<FusionGrid> build)
    {
        if (grid != null)
        {
            return grid;
        }

        var value = build();
        grid = new FusionGrid
        {
            PixelFootprint = value.PixelFootprint,
            RequestedVoxelSize = value.RequestedVoxelSize,
            VoxelSize = value.VoxelSize,
            Keys = new FusionCandidateGrid(value.Keys.Spans.ToArray(), value.Keys.Count, value.Keys.Axis)
        };
        GridBuilds++;
        return grid;
    }
}
