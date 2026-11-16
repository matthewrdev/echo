using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class AlignmentInputs
{
    public required int AlgorithmVersion
    {
        get; init;
    }
    public required ComputeBackend ComputeBackend
    {
        get; init;
    }
    public required string ComputeImplementation
    {
        get; init;
    }
    public required string Initial
    {
        get; init;
    }
    public required List<string> RecoveryUnits
    {
        get; init;
    }
    public required bool GuidedAlignmentPass
    {
        get; init;
    }
    public required int BundleIterations
    {
        get; init;
    }
}
