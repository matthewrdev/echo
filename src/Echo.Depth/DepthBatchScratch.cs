using System.Buffers;
using Echo.Geometry;

namespace Echo.Depth;

internal sealed class DepthBatchScratch : IDisposable
{
    private readonly double[] patches = [];
    private readonly double[] hypotheses = [];
    private readonly int refinementCount;
    private int sampleCount;

    public DepthBatchScratch(int capacity, int refinementCount, int maximumPlanes)
    {
        this.refinementCount = refinementCount;
        Local = new List<DepthSearchSample>(capacity);
        Global = new List<DepthSearchSample>(capacity);
        try
        {
            patches = ArrayPool<double>.Shared.Rent(checked(capacity * 25));
            hypotheses = ArrayPool<double>.Shared.Rent(checked(capacity * refinementCount));
            Costs = ArrayPool<float>.Shared.Rent(checked(capacity * maximumPlanes));
        }
        catch
        {
            ArrayPool<double>.Shared.Return(patches);
            ArrayPool<double>.Shared.Return(hypotheses);
            throw;
        }
    }

    public List<DepthSearchSample> Local
    {
        get;
    }
    public List<DepthSearchSample> Global
    {
        get;
    }
    public float[] Costs
    {
        get;
    }

    public void Reset()
    {
        Local.Clear();
        Global.Clear();
        sampleCount = 0;
    }

    public DepthSearchSample Sample(int pixelIndex, V3 ray, double norm, ReadOnlySpan<double> patch,
        ReadOnlySpan<double> refinement, double[] planes)
    {
        var patchOffset = checked(sampleCount * 25);
        patch.CopyTo(patches.AsSpan(patchOffset, 25));
        ReadOnlyMemory<double> depths = planes;
        if (!refinement.IsEmpty)
        {
            var depthOffset = checked(sampleCount * refinementCount);
            refinement.CopyTo(hypotheses.AsSpan(depthOffset, refinement.Length));
            depths = hypotheses.AsMemory(depthOffset, refinement.Length);
        }

        sampleCount++;
        return new DepthSearchSample
        {
            PixelIndex = pixelIndex,
            Ray = ray,
            Patch = patches.AsMemory(patchOffset, 25),
            Norm = norm,
            Hypotheses = depths
        };
    }

    public void Dispose()
    {
        Reset();
        ArrayPool<double>.Shared.Return(patches);
        ArrayPool<double>.Shared.Return(hypotheses);
        ArrayPool<float>.Shared.Return(Costs);
    }
}
