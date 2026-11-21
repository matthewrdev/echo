using Echo.Models;
using Echo.Pipeline.Features;
using Echo.Pipeline;

namespace Echo.Tests;

internal sealed class FixedImageWorkPlanner : IImageWorkPlanner
{
    private readonly int workers;
    private readonly long cacheBytes;

    public FixedImageWorkPlanner(int workers, long cacheBytes)
    {
        this.workers = workers;
        this.cacheBytes = cacheBytes;
    }

    public ImageWorkPlan Features(Settings settings, IReadOnlyList<Photo> images) => new(workers, 0, 0);
    public ImageWorkPlan Matching(Settings settings, int pairCount) => new(workers, 0, cacheBytes);
}
