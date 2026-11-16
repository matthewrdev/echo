using System.Text;
using Echo.Telemetry;
using Echo.Contracts;
using System.Buffers;
using Echo.Geometry;

namespace Echo.Depth.Metal;

internal sealed class MetalDepthCostSession : IDepthCostSession
{
    private readonly MetalDepthHandle handle;
    private readonly V3 origin;
    private readonly IPerformanceTrace trace;
    private readonly object gate = new();

    public MetalDepthCostSession(MetalDepthHandle handle, string device, string shaderHash, V3 origin, IPerformanceTrace trace)
    {
        this.handle = handle;
        this.origin = origin;
        this.trace = trace;
        Device = device;
        Implementation = "echo-metal-stereo-cost-2; relative origin; safe math; precise functions; fp-contract=off; shader-sha256=" + shaderHash;
    }

    public string Device
    {
        get;
    }
    public string Implementation
    {
        get;
    }

    public float[] Calculate(V3 centre, IReadOnlyList<DepthSearchSample> samples, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (samples.Count == 0)
        {
            return [];
        }
        using var span = trace.Begin("Stereo depth cost batch", PerformanceSpanKind.Compute, PerformanceBackend.Metal);
        var gpu = 0.0;
        var planeCount = samples[0].Hypotheses.Length;
        var costCount = checked(samples.Count * planeCount);
        var jobs = ArrayPool<float>.Shared.Rent(checked(samples.Count * 32));
        var depths = ArrayPool<float>.Shared.Rent(costCount);
        try
        {
            for (var index = 0; index < samples.Count; index++)
            {
                var sample = samples[index];
                if (sample.Hypotheses.Length != planeCount || sample.Patch.Length != 25)
                {
                    throw new ArgumentException("All samples in a depth batch must have the same hypothesis count and 25 patch values.", nameof(samples));
                }
                var job = jobs.AsSpan(index * 32, 32);
                job[0] = (float)(centre.X - origin.X);
                job[1] = (float)(centre.Y - origin.Y);
                job[2] = (float)(centre.Z - origin.Z);
                job[3] = (float)sample.Ray.X;
                job[4] = (float)sample.Ray.Y;
                job[5] = (float)sample.Ray.Z;
                job[6] = (float)sample.Norm;
                for (var element = 0; element < 25; element++)
                {
                    job[7 + element] = (float)sample.Patch[element];
                }
                for (var plane = 0; plane < planeCount; plane++)
                {
                    depths[index * planeCount + plane] = (float)sample.Hypotheses[plane];
                }
            }
            var costs = new float[costCount];
            lock (gate)
            {
                cancellation.ThrowIfCancellationRequested();
                var before = MetalDepthNative.GpuTime(handle);
                var error = new StringBuilder(2048);
                if (MetalDepthNative.Calculate(handle, jobs, samples.Count, depths, planeCount, costs, error, error.Capacity) != 0)
                {
                    throw new DepthAccelerationException(error.ToString());
                }
                gpu = MetalDepthNative.GpuTime(handle) - before;
            }
            cancellation.ThrowIfCancellationRequested();
            return costs;
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            span?.Measure(costCount, gpuMilliseconds: gpu);
            ArrayPool<float>.Shared.Return(jobs);
            ArrayPool<float>.Shared.Return(depths);
        }
    }

    public void Dispose()
    {
        handle.Dispose();
    }
}
