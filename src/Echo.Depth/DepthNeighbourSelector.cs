using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Depth;

[Export(typeof(IDepthNeighbourSelector)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DepthNeighbourSelector : IDepthNeighbourSelector
{
    private readonly ICameraGeometry geometry;

    [ImportingConstructor]
    public DepthNeighbourSelector(ICameraGeometry geometry)
    {
        this.geometry = geometry;
    }

    public CameraPose[] Select(ReconstructionScene scene, CameraPose reference, double medianDepth, int limit, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var capacity = Math.Min(Math.Max(0, limit), scene.Cameras.Count);
        if (capacity == 0)
        {
            return [];
        }

        // Keep only the closest K candidates in each group, preserving input order
        // on equal distances. No full-scene sort or growing intermediate lists.
        var buffer = new DepthNeighbourCandidate[checked(capacity * 2)];
        var nearest = buffer.AsSpan(0, capacity);
        var preferred = buffer.AsSpan(capacity, capacity);
        var nearestCount = 0;
        var preferredCount = 0;
        var preferredAvailable = 0;
        var centre = new V3(reference.Centre);
        var forward = geometry.Rotate(reference.Rotation, new V3(0, 0, 1));
        var minimumBaseline = Math.Clamp(medianDepth * .06, .75, 3);
        foreach (var camera in scene.Cameras)
        {
            cancellation.ThrowIfCancellationRequested();
            if (camera.ImageId == reference.ImageId)
            {
                continue;
            }
            var distance = (new V3(camera.Centre) - centre).Length;
            var facing = geometry.Rotate(camera.Rotation, new V3(0, 0, 1));
            var eligible = distance > .05 && facing.Dot(forward) > .8;
            if (!eligible)
            {
                continue;
            }
            var candidate = new DepthNeighbourCandidate(camera, distance);
            nearestCount = Insert(nearest, nearestCount, candidate);
            if (distance >= minimumBaseline)
            {
                preferredAvailable++;
                preferredCount = Insert(preferred, preferredCount, candidate);
            }
        }

        var selected = preferredAvailable >= 2 ? preferred[..preferredCount] : nearest[..nearestCount];
        var result = new CameraPose[selected.Length];
        for (var index = 0; index < selected.Length; index++)
        {
            result[index] = selected[index].Camera;
        }
        return result;
    }

    private int Insert(Span<DepthNeighbourCandidate> candidates, int count, DepthNeighbourCandidate candidate)
    {
        var index = count;
        while (index > 0 && candidate.Distance < candidates[index - 1].Distance)
        {
            index--;
        }
        if (index >= candidates.Length)
        {
            return count;
        }
        var newCount = Math.Min(count + 1, candidates.Length);
        candidates.Slice(index, newCount - index - 1).CopyTo(candidates[(index + 1)..]);
        candidates[index] = candidate;
        return newCount;
    }
}
