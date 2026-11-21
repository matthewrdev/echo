using Echo.Models;

namespace Echo.Fusion;

public interface IFusionChunkStore
{
    IEnumerable<VolumeSample> ReadOrBuild(long first, int count, Func<IList<VolumeSample>> build);
}
