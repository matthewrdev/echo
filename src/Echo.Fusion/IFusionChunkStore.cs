using Echo.Contracts;

namespace Echo.Fusion;

public interface IFusionChunkStore
{
    IEnumerable<VolumeSample> ReadOrBuild(int first, int count, Func<IList<VolumeSample>> build);
}
