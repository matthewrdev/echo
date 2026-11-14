namespace Echo.Contracts;

public sealed class ProcessingMetadata
{
    public int SchemaVersion { get; set; } = 1;
    public string Stage { get; set; } = "";
    public string AlgorithmVersion { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string OperatingSystem { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string SoftwareVersion { get; set; } = "";
    public List<ProcessingExecution> Executions { get; set; } = [];
    public List<string> SourceArtifacts { get; set; } = [];
    public long? MemoryBudgetBytes
    {
        get; set;
    }
    public int? ConcurrentImages
    {
        get; set;
    }
}
