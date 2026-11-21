using Echo.Fusion;
using Echo.Infrastructure;
using Echo.Pipeline.Runs;
using Echo.Storage.Pages;
using Echo.Storage;

namespace Echo.Pipeline.Fusion;

public sealed class FusionGridCheckpoints : IFusionGridStore
{
    private readonly RunContext context;
    private readonly object inputs;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;

    public FusionGridCheckpoints(RunContext context, object inputs, IFileOperations files, IJsonCodec json)
    {
        this.context = context;
        this.inputs = inputs;
        this.files = files;
        this.json = json;
    }

    public FusionGrid ReadOrBuild(Func<FusionGrid> build)
    {
        FusionGrid? generated = null;
        var path = context.Build("fusion-grid", inputs, directory =>
        {
            generated = build();
            var keys = generated.Keys;
            files.AtomicWrite(Path.Combine(directory, "ranges.bin"), stream => WriteRanges(stream, keys.Spans));
            files.AtomicJson(Path.Combine(directory, "grid.json"), new FusionGridMetadata(generated.PixelFootprint,
                generated.RequestedVoxelSize, generated.VoxelSize, keys.Count, keys.Spans.Count, keys.Axis));
        }, algorithmVersion: "echo-fusion-grid1");
        if (generated != null)
        {
            return generated;
        }

        var metadata = json.Read<FusionGridMetadata>(context.Store.PathFor(path + "/grid.json"));
        var valid = metadata.Cells >= 0 && metadata.Spans >= 0 && Enum.IsDefined(metadata.Axis)
            && double.IsFinite(metadata.VoxelSize) && metadata.VoxelSize > 0;
        if (!valid)
        {
            throw new InvalidDataException("Invalid cached fusion grid metadata.");
        }

        var spans = context.Data.Open(context.Store.PathFor(path + "/ranges.bin"), new GridSpanRecordCodec(),
            offset: 0, count: metadata.Spans);
        return new FusionGrid
        {
            PixelFootprint = metadata.PixelFootprint,
            RequestedVoxelSize = metadata.RequestedVoxelSize,
            VoxelSize = metadata.VoxelSize,
            Keys = new FusionCandidateGrid(spans, metadata.Cells, metadata.Axis)
        };
    }

    private void WriteRanges(Stream stream, IList<GridSpan> spans)
    {
        var codec = new GridSpanRecordCodec();
        var bytes = new byte[4096 * codec.Size];
        for (var first = 0; first < spans.Count; first += 4096)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var count = Math.Min(4096, spans.Count - first);
            var batch = bytes.AsSpan(0, count * codec.Size);
            if (spans is PagedList<GridSpan> paged)
            {
                paged.ReadEncodedRecords(first, batch);
            }
            else
            {
                for (var index = 0; index < count; index++)
                {
                    codec.Encode(spans[first + index], batch.Slice(index * codec.Size, codec.Size));
                }
            }

            stream.Write(batch);
        }
    }
}
