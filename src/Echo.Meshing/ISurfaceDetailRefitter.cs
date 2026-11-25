using Echo.Models;
using Echo.Storage.Resources;

namespace Echo.Meshing;

public interface ISurfaceDetailRefitter
{
    Surface Refit(Surface surface, Surface observed, double maximumDistance, CancellationToken cancellation, WorkBuffers? work = null);
}
