using Echo.Compute;
using Echo.Models;
using Echo.Telemetry;

namespace Echo.Texturing;

internal sealed class ObservedTexturePlacementSession : ITexturePlacementSession
{
    private readonly ITexturePlacementSession session;
    private readonly IComputeService compute;
    private readonly IPerformanceTrace? trace;
    private readonly ComputeExecution execution;

    public ObservedTexturePlacementSession(ITexturePlacementSession session, IComputeService compute, IPerformanceTrace? trace)
    {
        this.session = session;
        this.compute = compute;
        this.trace = trace;
        execution = new ComputeExecution
        {
            Operation = ComputeOperation.TexturePacking,
            Backend = session.Backend,
            Precision = ComputePrecision.IntegerExact,
            Device = session.Device,
            Implementation = session.Implementation
        };
    }

    public ComputeBackend Backend => session.Backend;
    public string Device => session.Device;
    public string Implementation => session.Implementation;

    public TextureMaskPlacement? Find(TextureAtlasOccupancy atlas, IReadOnlyList<TextureMask> masks,
        int maximumBottom, CancellationToken cancellation, int minimumBottom = 1)
    {
        using var span = trace?.Begin("Texture placement search", PerformanceSpanKind.Compute,
            Backend == ComputeBackend.Metal ? PerformanceBackend.Metal : PerformanceBackend.Cpu);
        var before = (session as IGpuTimingSource)?.CompletedGpuMilliseconds;
        try
        {
            var placement = session.Find(atlas, masks, maximumBottom, cancellation, minimumBottom);
            compute.RecordExecution(execution);
            return placement;
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
            span?.Measure(masks.Count, gpuMilliseconds: (session as IGpuTimingSource)?.CompletedGpuMilliseconds - before);
        }
    }

    public void Dispose() => session.Dispose();
}
