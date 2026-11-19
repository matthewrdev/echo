using Echo.Contracts;

namespace Echo.Validation;

public sealed class ValidationResult
{
    public int SchemaVersion { get; set; } = 1;
    public string Case { get; set; } = "";
    public string CaseSha256 { get; set; } = "";
    public string Status { get; set; } = "running";
    public string? Error
    {
        get; set;
    }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Version { get; set; } = "";
    public Dictionary<string, string> BinarySha256 { get; set; } = [];
    public string OperatingSystem { get; set; } = "";
    public string Architecture { get; set; } = "";
    public int ProcessorCount
    {
        get; set;
    }
    public long AvailableMemoryBytes
    {
        get; set;
    }
    public Settings Settings { get; set; } = new();
    public double PreparationSeconds
    {
        get; set;
    }
    public double ProcessingSeconds
    {
        get; set;
    }
    public long? PeakProcessWorkingSetBytes
    {
        get; set;
    }
    public long? PeakRegisteredWorkingSetBytes
    {
        get; set;
    }
    public long? PeakWorkingSetBytes => PeakProcessWorkingSetBytes.HasValue || PeakRegisteredWorkingSetBytes.HasValue
        ? Math.Max(PeakProcessWorkingSetBytes ?? 0, PeakRegisteredWorkingSetBytes ?? 0) : null;
    public string MemoryScope { get; set; } = "Maximum of the available OS-reported host peak and sampled processing peak including registered native workers. Unavailable counters are null; sampling can miss short peaks. A working budget is not a hard process limit.";
    public string QualityScope { get; set; } = "Regression health checks against a reviewed Echo baseline, not absolute geometric accuracy. Depth coverage is a per-image mean over the whole image, including sky. Inferred colour is not an inferred geometry measurement.";
    public string RunId { get; set; } = "";
    public string? ExportSha256
    {
        get; set;
    }
    public double? MaximumSeconds
    {
        get; set;
    }
    public long? MaximumPeakWorkingSetBytes
    {
        get; set;
    }
    public Dictionary<string, double> StageSeconds { get; set; } = [];
    public List<QualityCheck> Checks { get; set; } = [];
}
