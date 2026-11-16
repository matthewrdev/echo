using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Storage;

namespace Echo.Pipeline;

[Export(typeof(IRunContextFactory)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class RunContextFactory : IRunContextFactory
{
    private readonly ICheckpointStore checkpointStore;
    private readonly IJsonCodec jsonCodec;
    private readonly IFileOperations fileOperations;
    private readonly IBulkArtifactStore bulk;
    private readonly IProcessingResourcePlanner resources;

    [ImportingConstructor]
    public RunContextFactory(ICheckpointStore checkpointStore, IJsonCodec jsonCodec, IFileOperations fileOperations,
        IBulkArtifactStore bulk, IProcessingResourcePlanner resources)
    {
        this.checkpointStore = checkpointStore;
        this.jsonCodec = jsonCodec;
        this.fileOperations = fileOperations;
        this.bulk = bulk;
        this.resources = resources;
    }

    public RunContext Create(IProjectStore store, RunData run, CancellationToken cancellation, Action<object>? output)
    {
        return new RunContext(store, run, cancellation, output, checkpointStore, jsonCodec, fileOperations, bulk, resources.Plan(store.Load().Settings), resources.CreateTracker());
    }
}
