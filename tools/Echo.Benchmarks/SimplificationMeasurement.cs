namespace Echo.Benchmarks;

internal sealed record SimplificationMeasurement(int InputFaces, int OutputFaces, int CacheMb, double Seconds, double CollapseSeconds,
    long ReadBytes, long WrittenBytes, long Evictions, long CacheAccesses, long PeakResidentBytes, string OutputHash, double CpuSeconds);
