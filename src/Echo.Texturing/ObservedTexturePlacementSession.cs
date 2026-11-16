using Echo.Compute;
using Echo.Contracts;

namespace Echo.Texturing;

internal sealed class ObservedTexturePlacementSession : ITexturePlacementSession
{
    private readonly ITexturePlacementSession session;
    private readonly IComputeService compute;
    private readonly ComputeExecution execution;

    public ObservedTexturePlacementSession(ITexturePlacementSession session, IComputeService compute)
    {
        this.session = session;
        this.compute = compute;
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
        var placement = session.Find(atlas, masks, maximumBottom, cancellation, minimumBottom);
        compute.RecordExecution(execution);
        return placement;
    }

    public void Dispose() => session.Dispose();
}
