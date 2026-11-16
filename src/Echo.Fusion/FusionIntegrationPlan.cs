using Echo.Contracts;

namespace Echo.Fusion;

internal readonly record struct FusionIntegrationPlan(int BatchCapacity, int DepthCapacity)
{
    public ComputeBackend SelectBackend(ComputeBackend requested)
        => requested == ComputeBackend.Auto && BatchCapacity < 131072 ? ComputeBackend.Cpu : requested;
}
