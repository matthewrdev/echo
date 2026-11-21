using Echo.Models;

namespace Echo.Fusion;

internal readonly record struct FusionIntegrationPlan(int BatchCapacity, int DepthCapacity, int ResidentDepthCount = 0, int CheckpointCapacity = 0)
{
    public ComputeBackend SelectBackend(ComputeBackend requested)
        => requested == ComputeBackend.Auto && BatchCapacity < 131072 ? ComputeBackend.Cpu : requested;
}
