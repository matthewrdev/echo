namespace Echo.Storage.Pages;

public sealed record PagingStatistics(long ResidentBytes, long PeakResidentBytes, long ReadBytes,
    long WrittenBytes, long Hits, long Misses, long Evictions, long ScratchBytes);
