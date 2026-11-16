using Echo.Contracts;
using Echo.Telemetry;
using System.Buffers;
using System.Text;

namespace Echo.Texturing.Metal;

internal sealed unsafe class MetalTexturePlacementSession : ITexturePlacementSession, IGpuTimingSource
{
    private readonly MetalTextureHandle handle;
    private readonly int size;
    private readonly StringBuilder details = new(2048);
    private TextureAtlasOccupancy? uploadedAtlas;
    private long uploadedRevision;

    public MetalTexturePlacementSession(MetalTextureHandle handle, int size, string device, string implementation)
    {
        this.handle = handle;
        this.size = size;
        Device = device;
        Implementation = implementation;
    }

    public double CompletedGpuMilliseconds => MetalTextureNative.GpuTime(handle);

    public ComputeBackend Backend => ComputeBackend.Metal;
    public string Device
    {
        get;
    }
    public string Implementation
    {
        get;
    }

    public TextureMaskPlacement? Find(TextureAtlasOccupancy atlas, IReadOnlyList<TextureMask> masks,
        int maximumBottom, CancellationToken cancellation, int minimumBottom = 1)
    {
        cancellation.ThrowIfCancellationRequested();
        if (atlas.Size != size || masks.Count != 4)
        {
            throw new ArgumentException("Expected the session's atlas size and four ordered quarter-turn masks.");
        }

        Span<uint> descriptors = stackalloc uint[16];
        var spanLength = checked(masks.Sum(mask => mask.Spans.Length) * 3);
        var spans = ArrayPool<uint>.Shared.Rent(spanLength);
        try
        {
            var offset = 0;
            for (var rotation = 0; rotation < masks.Count; rotation++)
            {
                var mask = masks[rotation];
                if (mask.QuarterTurns != rotation || mask.Spans.Length == 0)
                {
                    throw new ArgumentException("Expected nonempty masks in quarter-turn order.");
                }
                descriptors[rotation * 4] = (uint)mask.Width;
                descriptors[rotation * 4 + 1] = (uint)mask.Height;
                descriptors[rotation * 4 + 2] = (uint)(offset / 3);
                descriptors[rotation * 4 + 3] = (uint)mask.Spans.Length;
                foreach (var span in mask.Spans)
                {
                    spans[offset++] = (uint)span.Y;
                    spans[offset++] = (uint)span.Start;
                    spans[offset++] = (uint)span.End;
                }
            }

            var sameAtlas = ReferenceEquals(uploadedAtlas, atlas);
            var unchanged = sameAtlas && uploadedRevision == atlas.Revision;
            var incremental = sameAtlas && uploadedRevision + 1 == atlas.Revision;
            var firstRow = incremental ? atlas.ChangedFirstRow : 0;
            var rowCount = unchanged ? 0 : incremental ? atlas.ChangedRowCount : atlas.Size;
            uint result;
            int status;
            fixed (uint* occupancyPointer = atlas.Words, descriptorPointer = descriptors, spanPointer = spans)
            {
                status = MetalTextureNative.Find(handle, occupancyPointer, firstRow, rowCount, descriptorPointer, spanPointer,
                    spanLength / 3, minimumBottom, Math.Min(size, maximumBottom), out result, details, details.Capacity);
            }
            cancellation.ThrowIfCancellationRequested();
            if (status != 0)
            {
                throw new TextureAccelerationException(details.ToString());
            }
            uploadedAtlas = atlas;
            uploadedRevision = atlas.Revision;
            if (result == uint.MaxValue)
            {
                return null;
            }

            var x = (int)(result % size);
            var rotationIndex = (int)(result / size % 4);
            var bottom = (int)(result / size / 4);
            return new TextureMaskPlacement(x, bottom - masks[rotationIndex].Height, rotationIndex);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(spans);
        }
    }

    public void Dispose()
    {
        handle.Dispose();
    }
}
