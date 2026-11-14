namespace Echo.Contracts;

public sealed class ProcessingExecution
{
    public string Component { get; set; } = "";
    public string RequestedBackend { get; set; } = "cpu";
    public string ActualBackend { get; set; } = "cpu";
    public string Implementation { get; set; } = "Echo managed processing";
    public string Precision { get; set; } = "stage-defined";
    public string Validation { get; set; } = "none";
    public string OutputAuthority { get; set; } = "backend";
    public string? CpuImplementation
    {
        get; set;
    }
    public string? Device
    {
        get; set;
    }
    public string? FallbackReason
    {
        get; set;
    }
    public int? LevelWidth
    {
        get; set;
    }
    public int? Workers
    {
        get; set;
    }
    public long AcceleratedSearches
    {
        get; set;
    }
    public long CpuSearches
    {
        get; set;
    }
    public long ValidatedSearches
    {
        get; set;
    }
    public long MatchedSearches
    {
        get; set;
    }
    public long CorrectedSearches
    {
        get; set;
    }
}
