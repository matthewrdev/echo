using Echo.Models;
using Echo.Storage;

namespace Echo.SurfaceCompletion;

public interface ISurfaceReconstructor
{
    Surface Reconstruct(Surface measured, ReconstructionScene scene, double voxelSize,
        CancellationToken cancellation, Action<string>? progress = null, ProcessingMemoryTracker? memory = null, WorkBuffers? work = null);
}
