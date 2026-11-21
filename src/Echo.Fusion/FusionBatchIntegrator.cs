using System.Buffers;
using Echo.Compute;
using Echo.Geometry;
using Echo.Models;

namespace Echo.Fusion;

internal sealed class FusionBatchIntegrator : IDisposable
{
    private readonly IReadOnlyList<DepthView> views;
    private readonly V3 origin;
    private readonly double tolerance;
    private readonly double voxel;
    private readonly CancellationToken cancellation;
    private readonly ComputeBackend backend;
    private readonly FusionDepthTiles tiles;
    private FusionAccumulator[]? accumulators;
    private readonly VolumeSample[] output;
    private readonly Action<string>? progress;
    private IComputeSession? session;
    private FusionCompute? gpu;
    private bool disposed;

    public FusionBatchIntegrator(IReadOnlyList<DepthView> views, double voxel, double tolerance, FusionIntegrationPlan plan,
        IComputeService? compute, ComputeBackend backend, CancellationToken cancellation, Action<string>? progress = null, Action<int, int>? preparationProgress = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(plan.BatchCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(plan.DepthCapacity, 4);
        this.views = views;
        this.voxel = voxel;
        this.tolerance = tolerance;
        this.backend = backend;
        this.cancellation = cancellation;
        this.progress = progress;
        origin = views.Count == 0 ? default : views[0].Centre;
        Capacity = plan.BatchCapacity;
        Cells = ArrayPool<Grid>.Shared.Rent(Capacity);
        output = ArrayPool<VolumeSample>.Shared.Rent(Capacity);
        tiles = new FusionDepthTiles(plan.DepthCapacity, cancellation);
        try
        {
            OpenCompute(compute, plan, preparationProgress);
            progress?.Invoke(gpu != null
            ? $"Integrating with Metal using {(gpu.HasResidentDepths ? "resident depth maps" : "bounded depth tiles")} and batches of up to {Capacity:N0} grid samples."
            : $"Integrating on CPU using bounded depth tiles and batches of up to {Capacity:N0} grid samples.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OpenCompute(IComputeService? compute, FusionIntegrationPlan plan, Action<int, int>? preparationProgress)
    {
        if (compute == null && backend == ComputeBackend.Metal)
        {
            throw new ComputeException("Metal processing was requested but no compute service is available.");
        }
        if (compute != null && backend != ComputeBackend.Cpu)
        {
            try
            {
                session = compute.Open(backend, cancellation);
                if (session != null)
                {
                    gpu = new FusionCompute(session, plan, views.Count);
                    if (plan.ResidentDepthCount > 0)
                    {
                        var capacity = Math.Min(262144, plan.ResidentDepthCount);
                        var staging = ArrayPool<float>.Shared.Rent(capacity);
                        try
                        {
                            gpu.LoadResidentDepths(views, staging.AsSpan(0, capacity), cancellation, preparationProgress);
                        }
                        finally
                        {
                            ArrayPool<float>.Shared.Return(staging);
                        }
                    }
                }
            }
            catch (ComputeException) when (backend == ComputeBackend.Auto)
            {
                gpu?.Dispose();
                gpu = null;
                session?.Dispose();
                session = null;
            }
            catch
            {
                session?.Dispose();
                throw;
            }
        }
    }

    public int Capacity
    {
        get;
    }
    public Grid[] Cells
    {
        get;
    }

    public ArraySegment<VolumeSample> Integrate(int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (count < 1 || count > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        cancellation.ThrowIfCancellationRequested();
        var bounds = FusionBoundsBuilder.Measure(Cells.AsSpan(0, count), voxel);
        if (gpu != null)
        {
            try
            {
                gpu.Begin(Cells.AsSpan(0, count), voxel, origin, tolerance);
                if (gpu.HasResidentDepths)
                {
                    gpu.AddResident(bounds, origin, count);
                }
                else
                {
                    tiles.Start(views, bounds, origin);
                    while (tiles.LoadNext())
                    {
                        gpu.Add(tiles, origin, count);
                    }
                }
                return new ArraySegment<VolumeSample>(output, 0, gpu.Finish(Cells.AsSpan(0, count), output, cancellation));
            }
            catch (ComputeException) when (backend == ComputeBackend.Auto)
            {
                gpu.Dispose();
                gpu = null;
                session!.Dispose();
                session = null;
                progress?.Invoke("Metal integration could not continue. Repeating the unfinished batch on CPU.");
            }
        }
        var cpuAccumulators = accumulators ??= ArrayPool<FusionAccumulator>.Shared.Rent(Capacity);
        cpuAccumulators.AsSpan(0, count).Clear();
        tiles.Start(views, bounds, origin);
        while (tiles.LoadNext())
        {
            Parallel.For(0, (count + 1023) / 1024, new ParallelOptions
            {
                CancellationToken = cancellation,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, block =>
            {
                var first = block * 1024;
                var length = Math.Min(count - first, 1024);
                FusionCpuBlockIntegrator.Integrate(Cells.AsSpan(first, length), cpuAccumulators.AsSpan(first, length),
                    tiles.Windows.AsSpan(0, tiles.Count), tiles.Depths, voxel, tolerance, origin);
            });
        }
        var retained = 0;
        for (var index = 0; index < count; index++)
        {
            var value = cpuAccumulators[index];
            if (value.Support >= 2)
            {
                var key = Cells[index];
                output[retained++] = new VolumeSample
                {
                    X = key.X,
                    Y = key.Y,
                    Z = key.Z,
                    Distance = (float)(value.Sum / value.Weight),
                    Views = value.Support
                };
            }
        }
        return new ArraySegment<VolumeSample>(output, 0, retained);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            gpu?.Dispose();
            session?.Dispose();
        }
        finally
        {
            tiles.Dispose();
            ArrayPool<Grid>.Shared.Return(Cells);
            ArrayPool<VolumeSample>.Shared.Return(output);
            if (accumulators != null)
            {
                ArrayPool<FusionAccumulator>.Shared.Return(accumulators);
            }
        }
    }
}
