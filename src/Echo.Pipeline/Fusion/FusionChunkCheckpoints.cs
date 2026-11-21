using Echo.Fusion;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;

namespace Echo.Pipeline.Fusion;

public sealed class FusionChunkCheckpoints : IFusionChunkStore
{
    private readonly RunContext context;
    private readonly object inputs;
    private readonly IFileOperations files;

    public FusionChunkCheckpoints(RunContext context, object inputs, IFileOperations files)
    {
        this.context = context;
        this.inputs = inputs;
        this.files = files;
    }

    public IEnumerable<VolumeSample> ReadOrBuild(long first, int count, Func<IList<VolumeSample>> build)
    {
        IList<VolumeSample>? generated = null;
        var path = context.Build("fusion-chunk", new
        {
            inputs,
            first,
            count,
            formatVersion = 2
        }, directory =>
        {
            generated = build();
            files.AtomicData(Path.Combine(directory, "volume.json"), new FusedVolume { Samples = generated }, context.Cancellation);
        });
        // Return an in-memory chunk, whose lifetime ends after the caller appends
        // it. Opening one persistent session handle per chunk would grow forever.
        return generated ?? context.ReadArtifactChunk(path);
    }
}
