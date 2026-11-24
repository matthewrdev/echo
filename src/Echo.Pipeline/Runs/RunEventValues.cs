using System.Globalization;
using Echo.Models;

namespace Echo.Pipeline.Runs;

internal static class RunEventValues
{
    internal static Dictionary<string, object?> Resources(ResourceProgress value) => new()
    {
        ["processor"] = value.Processor is { } processor ? Processor(processor) : null,
        ["sampledAt"] = value.SampledAt.ToString("O", CultureInfo.InvariantCulture),
        ["workingSetBytes"] = value.WorkingSetBytes,
        ["peakWorkingSetBytes"] = value.PeakWorkingSetBytes,
        ["managedBytes"] = value.ManagedBytes,
        ["memoryBudgetBytes"] = value.MemoryBudgetBytes,
        ["cacheResidentBytes"] = value.CacheResidentBytes,
        ["cachePeakBytes"] = value.CachePeakBytes,
        ["diskReadBytes"] = value.DiskReadBytes,
        ["diskWrittenBytes"] = value.DiskWrittenBytes,
        ["pageEvictions"] = value.PageEvictions,
        ["scratchBytes"] = value.ScratchBytes
    };

    internal static Dictionary<string, object?> Processing(ArtifactProcessingReference value) => new()
    {
        ["stage"] = value.Stage,
        ["metadataPath"] = value.MetadataPath,
        ["reused"] = value.Reused,
        ["provenanceStatus"] = value.ProvenanceStatus
    };

    private static Dictionary<string, object?> Processor(ProcessorProgress value) => new()
    {
        ["cpuPercent"] = value.CpuPercent,
        ["logicalProcessors"] = value.LogicalProcessors,
        ["threads"] = value.Threads?.Select(Thread).ToArray()
    };

    private static Dictionary<string, object?> Thread(ThreadProgress value) => new()
    {
        ["processId"] = value.ProcessId,
        ["threadId"] = value.ThreadId,
        ["name"] = value.Name,
        ["cpuPercent"] = value.CpuPercent
    };
}
