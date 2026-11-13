using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Pipeline;

[Export(typeof(IRunContextFactory)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class RunContextFactory : IRunContextFactory
{
    private readonly ICheckpointStore checkpointStore;
    private readonly IJsonCodec jsonCodec;
    private readonly IFileOperations fileOperations;

    [ImportingConstructor]
    public RunContextFactory(ICheckpointStore checkpointStore, IJsonCodec jsonCodec, IFileOperations fileOperations)
    {
        this.checkpointStore = checkpointStore;
        this.jsonCodec = jsonCodec;
        this.fileOperations = fileOperations;
    }

    public RunContext Create(IProjectStore store, RunData run, CancellationToken cancellation, Action<object>? output)
    {
        return new RunContext(store, run, cancellation, output, checkpointStore, jsonCodec, fileOperations);
    }
}
