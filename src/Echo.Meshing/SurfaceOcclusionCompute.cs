using System.Buffers;
using Echo.Compute;
using Echo.Models;
using Echo.Geometry;

namespace Echo.Meshing;

// Metal finds a bounded set of possible blockers. Double precision CPU tests
// retain endpoint and shared-edge semantics; overflowing queries use the CPU BVH.
internal sealed class SurfaceOcclusionCompute : IDisposable
{
    internal const int BatchSize = 4096;
    internal const int ResultStride = 65;
    private readonly IComputeSession session;
    private readonly SurfaceTriangleIndex index;
    private readonly V3 origin;
    private readonly bool ownsSession;
    private readonly ComputeBackend backend;
    private bool failed;

    internal SurfaceOcclusionCompute(IComputeSession session, SurfaceTriangleIndex index, V3 origin, bool ownsSession, ComputeBackend backend)
    {
        this.session = session;
        this.index = index;
        this.origin = origin;
        this.ownsSession = ownsSession;
        this.backend = backend;
    }

    public void Evaluate(ReadOnlySpan<SurfaceRay> rays, Span<bool> hidden, CancellationToken cancellation)
    {
        var jobs = ArrayPool<float>.Shared.Rent(BatchSize * 8);
        var matches = ArrayPool<int>.Shared.Rent(BatchSize * ResultStride);
        var queries = ArrayPool<SurfaceRay>.Shared.Rent(BatchSize);
        var results = ArrayPool<bool>.Shared.Rent(BatchSize);
        var parallel = new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        };
        try
        {
            for (var start = 0; start < rays.Length; start += BatchSize)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(BatchSize, rays.Length - start);
                rays.Slice(start, count).CopyTo(queries);
                if (!failed)
                {
                    for (var ray = 0; ray < count; ray++)
                    {
                        var from = rays[start + ray].Start - origin;
                        var to = rays[start + ray].End - origin;
                        var slot = ray * 8;
                        jobs[slot] = (float)from.X;
                        jobs[slot + 1] = (float)from.Y;
                        jobs[slot + 2] = (float)from.Z;
                        jobs[slot + 3] = 0;
                        jobs[slot + 4] = (float)to.X;
                        jobs[slot + 5] = (float)to.Y;
                        jobs[slot + 6] = (float)to.Z;
                        jobs[slot + 7] = 0;
                    }
                    try
                    {
                        session.Upload<float>(4, jobs.AsSpan(0, count * 8));
                        session.Allocate(3, count * ResultStride * sizeof(int));
                        session.Dispatch(ComputeKernel.SurfaceOcclusion, count, [index.Nodes.Count, ResultStride]);
                        session.Read<int>(3, matches.AsSpan(0, count * ResultStride));
                    }
                    catch (ComputeException) when (backend == ComputeBackend.Auto)
                    {
                        failed = true;
                    }
                }
                Parallel.For(0, count, parallel, ray =>
                {
                    var query = queries[ray];
                    var matchCount = failed ? -1 : matches[ray * ResultStride];
                    var blocked = matchCount < 0 && index.HasOccluder(query.Start, query.End);
                    for (var match = 0; match < matchCount && !blocked; match++)
                    {
                        var face = matches[ray * ResultStride + 1 + match];
                        blocked = SurfaceTriangleIntersection.Crosses(query.Start, query.End, index.Triangle(face));
                    }
                    results[ray] = blocked;
                });
                results.AsSpan(0, count).CopyTo(hidden[start..]);
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(jobs);
            ArrayPool<int>.Shared.Return(matches);
            ArrayPool<SurfaceRay>.Shared.Return(queries);
            ArrayPool<bool>.Shared.Return(results);
        }
    }

    public void Dispose()
    {
        if (ownsSession)
        {
            session.Dispose();
        }
    }
}
