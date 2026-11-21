using Echo.Models;
using Echo.Storage;

namespace Echo.SurfaceCompletion;

public interface IPoissonSurfaceCodec
{
    void WriteSamples(string path, IReadOnlyList<OrientedSurfaceSample> samples, CancellationToken cancellation);
    Surface Read(string path, CancellationToken cancellation, WorkBuffers? work = null);
}
