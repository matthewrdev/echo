using System.Text;

namespace Echo.Texturing.Metal;

internal sealed class MetalTexturePlacementSession : ITexturePlacementSession
{
    private readonly MetalTextureHandle handle;
    private readonly int size;

    public MetalTexturePlacementSession(MetalTextureHandle handle, int size, string device)
    {
        this.handle = handle;
        this.size = size;
        Backend = "Metal (" + device + ")";
    }

    public string Backend
    {
        get;
    }

    public TextureMaskPlacement? Find(TextureAtlasOccupancy atlas, IReadOnlyList<TextureMask> masks,
        int maximumBottom, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (atlas.Size != size || masks.Count != 4)
        {
            throw new ArgumentException("Expected the session's atlas size and four ordered quarter-turn masks.");
        }

        var descriptors = new uint[16];
        var spans = new uint[checked(masks.Sum(mask => mask.Spans.Length) * 3)];
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

        var details = new StringBuilder(2048);
        var status = MetalTextureNative.Find(handle, atlas.Words, descriptors, spans, spans.Length / 3,
            Math.Min(size, maximumBottom), out var result, details, details.Capacity);
        cancellation.ThrowIfCancellationRequested();
        if (status != 0)
        {
            throw new TextureAccelerationException(details.ToString());
        }
        if (result == uint.MaxValue)
        {
            return null;
        }

        var x = (int)(result % size);
        var rotationIndex = (int)(result / size % 4);
        var bottom = (int)(result / size / 4);
        return new TextureMaskPlacement(x, bottom - masks[rotationIndex].Height, rotationIndex);
    }

    public void Dispose()
    {
        handle.Dispose();
    }
}
