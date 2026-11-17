namespace Echo.Benchmarks;

internal sealed record FusionResumeMeasurement(int Iteration, int Points, int Cells, double Seconds, long AllocatedBytes,
    long ReadBytes, long WrittenBytes, long UploadedBytes, long ReadbackBytes, long PeakWorkingSetBytes,
    int ChunkBuilds, int GridBuilds, string OutputHash);
