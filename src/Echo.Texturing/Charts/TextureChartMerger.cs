using System.Diagnostics;
using Echo.Compute;
using Echo.Models;
using Echo.Texturing.Layout;
using Echo.Telemetry;

namespace Echo.Texturing.Charts;

internal static class TextureChartMerger
{
    public static List<TexturePatch> Merge(TextureChartGeometry geometry, List<TexturePatch> patches, int maximumFaces,
        IReadOnlyList<int>? materials, CancellationToken cancellation, Action<string>? progress, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto, IPerformanceTrace? trace = null)
    {
        var owner = new int[geometry.Surface.Faces.Count];
        for (var index = 0; index < patches.Count; index++)
        {
            foreach (var face in patches[index].Faces)
            {
                owner[face] = index;
            }
        }

        var versions = new int[patches.Count];
        var rejected = new HashSet<TextureChartMergeKey>();
        var active = patches.Select(patch => (TexturePatch?)patch).ToArray();
        var merged = 0;
        var attempts = 0;
        var solveTicks = 0L;
        var validationTicks = 0L;
        var neighbours = new Dictionary<int, double>();
        var scratch = new TextureLinearSystem(0);
        for (var pass = 0; pass < 4; pass++)
        {
            var previous = merged;
            var order = Enumerable.Range(0, active.Length).Where(index => active[index] != null)
                .OrderBy(index => active[index]!.Faces.Count).ToArray();
            foreach (var index in order)
            {
                cancellation.ThrowIfCancellationRequested();
                var patch = active[index];
                if (patch == null || patch.Faces.Count >= maximumFaces)
                {
                    continue;
                }

                neighbours.Clear();
                foreach (var face in patch.Faces)
                {
                    for (var edge = 0; edge < 3; edge++)
                    {
                        var adjacent = geometry.Neighbours[face * 3 + edge];
                        if (adjacent < 0 || owner[adjacent] == index)
                        {
                            continue;
                        }

                        var other = owner[adjacent];
                        var differentMaterial = materials != null && materials[face] != materials[adjacent];
                        if (!differentMaterial && patch.Faces.Count + active[other]!.Faces.Count <= maximumFaces)
                        {
                            var vertices = geometry.Surface.Faces[face];
                            var length = (geometry.Points[vertices[edge]] - geometry.Points[vertices[(edge + 1) % 3]]).Length;
                            neighbours[other] = neighbours.GetValueOrDefault(other) + length;
                        }
                    }
                }

                foreach (var other in neighbours.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).Take(4).Select(entry => entry.Key))
                {
                    var key = index < other
                        ? new TextureChartMergeKey(index, versions[index], other, versions[other])
                        : new TextureChartMergeKey(other, versions[other], index, versions[index]);
                    if (!rejected.Add(key))
                    {
                        continue;
                    }

                    var faces = patch.Faces.Concat(active[other]!.Faces).ToList();
                    var chart = new TextureChartMesh(geometry, faces);
                    attempts++;
                    var started = Stopwatch.GetTimestamp();
                    var coordinates = TextureConformalMap.Solve(chart, cancellation, compute, backend, scratch);
                    var solved = Stopwatch.GetTimestamp();
                    var accepted = coordinates != null && TextureChartValidation.Accept(chart, coordinates, cancellation);
                    solveTicks += solved - started;
                    validationTicks += Stopwatch.GetTimestamp() - solved;
                    if (!accepted)
                    {
                        continue;
                    }

                    var union = TextureChartPatch.Create(geometry, chart, faces, coordinates!);
                    active[index] = union;
                    active[other] = null;
                    versions[index]++;
                    foreach (var face in faces)
                    {
                        owner[face] = index;
                    }

                    merged++;
                    break;
                }
            }

            progress?.Invoke($"Merging compatible UV charts: pass {pass + 1}/4, {patches.Count - merged:N0} charts remaining.");
            if (merged == previous)
            {
                break;
            }
        }

        trace?.Measurement("Rejected texture chart merges", attempts - merged, "charts");
        trace?.Measurement("Texture chart merge solve time", Stopwatch.GetElapsedTime(0, solveTicks).TotalMilliseconds, "ms");
        trace?.Measurement("Texture chart merge validation time", Stopwatch.GetElapsedTime(0, validationTicks).TotalMilliseconds, "ms");
        return active.Where(patch => patch != null).Select(patch => patch!).ToList();
    }
}
