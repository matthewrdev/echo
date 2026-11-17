namespace Echo.Benchmarks;

internal sealed record MeshIndexMeasurement(int Iteration, int Faces, long BudgetBytes, double BuildSeconds, double QuerySeconds,
    long AllocatedBytes, long WorkingSetBytes, long PeakWorkingSetBytes, long AccountedPeakBytes,
    long ReadBytes, long WrittenBytes, double QueryChecksum);
