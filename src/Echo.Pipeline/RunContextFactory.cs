using Echo.Contracts;
using Echo.Compute;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Storage;
using Echo.Telemetry;

namespace Echo.Pipeline;

public sealed class RunContextFactory : IRunContextFactory
{
    private readonly ICheckpointStore checkpointStore;
    private readonly IJsonCodec jsonCodec;
    private readonly IFileOperations fileOperations;
    private readonly IBulkArtifactStore bulk;
    private readonly IProcessingResourcePlanner resources;
    private readonly IProcessTelemetryReader telemetry;
    private readonly IComputeService? compute;
    private readonly IPerformanceTrace trace;
    private readonly IPerformanceTraceStore traces;

    public RunContextFactory(ICheckpointStore checkpointStore, IJsonCodec jsonCodec, IFileOperations fileOperations,
        IBulkArtifactStore bulk, IProcessingResourcePlanner resources, IProcessTelemetryReader telemetry, IPerformanceTrace trace, IPerformanceTraceStore traces, IComputeService? compute = null)
    {
        this.checkpointStore = checkpointStore;
        this.jsonCodec = jsonCodec;
        this.fileOperations = fileOperations;
        this.bulk = bulk;
        this.resources = resources;
        this.telemetry = telemetry;
        this.compute = compute;
        this.trace = trace;
        this.traces = traces;
    }

    public RunContext Create(IProjectStore store, RunData run, CancellationToken cancellation, Action<object>? output)
    {
        return new RunContext(store, run, cancellation, output, checkpointStore, jsonCodec, fileOperations, bulk, resources,
            resources.Plan(store.Load().Settings), resources.CreateTracker(), new ProcessingTelemetryTracker(telemetry), trace, traces, compute);
    }
}
