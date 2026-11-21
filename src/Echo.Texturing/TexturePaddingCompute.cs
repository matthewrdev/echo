using Echo.Models;
using Echo.Compute;
using SkiaSharp;

namespace Echo.Texturing;

internal static class TexturePaddingCompute
{
    public static bool TryExtend(IComputeService? compute, ComputeBackend backend, Span<SKColor> pixels, int size, int gutter, CancellationToken cancellation)
    {
        // Very wide gutters favour a CPU boundary scan over a quadratic window.
        var suitableSize = gutter > 0 && gutter <= 32 && size >= 256;
        if (compute == null || !suitableSize)
        {
            return false;
        }
        if (!compute.CanAllocate((long)pixels.Length * 2 * sizeof(uint), backend))
        {
            return false;
        }
        try
        {
            using var session = compute.Open(backend, cancellation);
            if (session == null)
            {
                return false;
            }
            session.Upload<SKColor>(0, pixels);
            session.Allocate(1, checked(pixels.Length * sizeof(uint)));
            session.Dispatch(ComputeKernel.TexturePadding, pixels.Length, [size, gutter]);
            session.Read<SKColor>(1, pixels);
            return true;
        }
        catch (ComputeException) when (backend == ComputeBackend.Auto)
        {
            return false;
        }
    }
}
