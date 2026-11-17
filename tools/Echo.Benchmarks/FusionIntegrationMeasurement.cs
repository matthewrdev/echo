namespace Echo.Benchmarks;

internal sealed record FusionIntegrationMeasurement(FusionIntegrationBenchmarkMode Mode, int Cells, int Retained,
    double Seconds, long AllocatedBytes, long ReadBytes, bool UsedMetal, long SupportSum, double DistanceSum, double PreparationSeconds, int ResidentDepthCount, string OutputHash, long UploadedBytes, long ReadbackBytes);
