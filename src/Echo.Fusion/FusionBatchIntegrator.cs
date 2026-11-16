using System.Buffers;
using Echo.Compute;
using Echo.Contracts;
using Echo.Geometry;

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
    private readonly FusionAccumulator[] accumulators;
    private readonly VolumeSample[] output;
    private readonly Action<string>? progress;
    private IComputeSession? session;
    private FusionCompute? gpu;
    private bool disposed;

    public FusionBatchIntegrator(IReadOnlyList<DepthView> views, double voxel, double tolerance, FusionIntegrationPlan plan,
        IComputeService? compute, ComputeBackend backend, CancellationToken cancellation, Action<string>? progress = null)
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
        accumulators = ArrayPool<FusionAccumulator>.Shared.Rent(Capacity);
        tiles = new FusionDepthTiles(plan.DepthCapacity, cancellation);
        try
        {
            OpenCompute(compute, plan);
            progress?.Invoke(gpu != null
            ? $"Integrating with Metal using bounded depth tiles and batches of up to {Capacity:N0} grid samples."
            : $"Integrating on CPU using bounded depth tiles and batches of up to {Capacity:N0} grid samples.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OpenCompute(IComputeService? compute, FusionIntegrationPlan plan)
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
                    gpu = new FusionCompute(session, plan);
                }
            }
            catch (ComputeException) when (backend == ComputeBackend.Auto)
            {
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
                tiles.Start(views, bounds, origin);
                gpu.Begin(Cells.AsSpan(0, count), voxel, origin, tolerance);
                while (tiles.LoadNext())
                {
                    gpu.Add(tiles, origin, count);
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
        accumulators.AsSpan(0, count).Clear();
        tiles.Start(views, bounds, origin);
        while (tiles.LoadNext())
        {
            Parallel.For(0, (count + 1023) / 1024, new ParallelOptions
            {
                CancellationToken = cancellation,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, block =>
            {
                var end = Math.Min(count, (block + 1) * 1024);
                for (var index = block * 1024; index < end; index++)
                {
                    var key = Cells[index];
                    var world = new V3(key.X * voxel, key.Y * voxel, key.Z * voxel);
                    ref var accumulator = ref accumulators[index];
                    for (var view = 0; view < tiles.Count; view++)
                    {
                        var window = tiles.Windows[view];
                        accumulator.Add(window.Observe(world, tolerance, tiles.Depths), window.View.Focal, tolerance, voxel);
                    }
                }
            });
        }
        var retained = 0;
        for (var index = 0; index < count; index++)
        {
            var value = accumulators[index];
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
            ArrayPool<FusionAccumulator>.Shared.Return(accumulators);
        }
    }
}
