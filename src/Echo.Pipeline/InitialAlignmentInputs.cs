using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class InitialAlignmentInputs
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
    public required List<string> PairUnits
    {
        get; init;
    }
    public required IReadOnlyList<Photo> Cameras
    {
        get; init;
    }
    public required int BundleIterations
    {
        get; init;
    }
    public required List<Hint> Hints
    {
        get; init;
    }
}
