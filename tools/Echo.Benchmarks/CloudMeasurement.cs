namespace Echo.Benchmarks;

internal sealed class CloudMeasurement
{
    public required int Iteration
    {
        get; init;
    }
    public required double Seconds
    {
        get; init;
    }
    public required long AllocatedBytes
    {
        get; init;
    }
    public required int CameraCount
    {
        get; init;
    }
    public required int CloudWorkerThreads
    {
        get; init;
    }
    public required int CloudBatchRows
    {
        get; init;
    }
    public required int InputSamples
    {
        get; init;
    }
    public required int Retained
    {
        get; init;
    }
    public required string Hash
    {
        get; init;
    }
}
