using Echo.Models;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Projects;
using Echo.SceneSemantics;

namespace Echo.Depth;

public sealed class CliffDepthRecovery : ICliffDepthRecovery
{
    private readonly IRasterImageService images;

    public CliffDepthRecovery(IRasterImageService images)
    {
        this.images = images;
    }

    public CliffRecoveryResult Recover(IProjectStore store, ProjectData project, ReconstructionScene scene,
        DepthMap source, IReadOnlyList<DepthMap> originalMaps, SemanticMap semantics, CancellationToken cancellation,
        Action<string>? progress = null)
    {
        var photos = project.Images.ToDictionary(photo => photo.Id);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId);
        var maps = originalMaps.ToDictionary(map => map.ImageId);
        var photo = photos[source.ImageId];
        var camera = cameras[source.ImageId];
        var reference = new DepthView(photo, camera, source);
        var result = new DepthMap
        {
            ImageId = source.ImageId,
            Width = source.Width,
            Height = source.Height,
            Depth = Enumerable.Range(0, source.SampleCount).Select(source.DepthAt).ToArray(),
            Confidence = Enumerable.Range(0, source.SampleCount).Select(source.ConfidenceAt).ToArray()
        };
        var valid = result.Depth.Where(value => float.IsFinite(value) && value > 0).Order().ToArray();
        if (valid.Length == 0)
        {
            return new CliffRecoveryResult { Map = result };
        }

        var range = new DepthSearchRange(valid[0], valid[^1], valid[valid.Length / 2], valid.Length, "accepted depth");
        var selected = DepthNeighbourSelector.Select(project, scene, camera, range, Math.Max(6, project.Settings.DepthNeighbours), cancellation, source)
            .Where(other => maps.ContainsKey(other.ImageId)).ToArray();
        if (selected.Length < 2)
        {
            return new CliffRecoveryResult { Map = result };
        }

        var verification = selected.Select(other => new DepthView(photos[other.ImageId], other, maps[other.ImageId])).ToArray();
        using var sources = new DepthSourceImages(images, store, selected.Length + 1);
        var width = Math.Min(photo.Width, Math.Min(4096, source.Width * 2));
        var fine = sources.ReadLevel(photo, width);
        var focal = CameraGeometry.Focal(photo, camera) * fine.Width / photo.Width;
        var right = CameraGeometry.Rotate(camera.Rotation, new V3(1 / focal, 0, 0));
        var down = CameraGeometry.Rotate(camera.Rotation, new V3(0, 1 / focal, 0));
        var views = new StereoView[selected.Length];
        for (var index = 0; index < views.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var other = selected[index];
            var otherPhoto = photos[other.ImageId];
            var gray = sources.ReadLevel(otherPhoto, width);
            var inverse = CameraGeometry.Transpose(other.Rotation);
            views[index] = new StereoView
            {
                Centre = new V3(other.Centre),
                WorldToCamera = inverse,
                Width = gray.Width,
                Height = gray.Height,
                Gray = gray.Pixels,
                Focal = CameraGeometry.Focal(otherPhoto, other) * gray.Width / otherPhoto.Width,
                PatchRight = CameraGeometry.Rotate(inverse, right),
                PatchDown = CameraGeometry.Rotate(inverse, down)
            };
        }

        var searched = 0;
        var recovered = 0;
        progress?.Invoke($"Searching bounded rock gaps with {fine.Width}px photographs and {views.Length} neighbours; preserving all original samples.");
        Parallel.For(4, source.Height - 4, new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) }, y =>
        {
            Span<double> patch = stackalloc double[25];
            Span<double> costs = stackalloc double[33];
            Span<double> hypotheses = stackalloc double[33];
            var sampler = new GrayscaleSampler(fine.Pixels, fine.Width, fine.Height);
            for (var x = 4; x < source.Width - 4; x++)
            {
                cancellation.ThrowIfCancellationRequested();
                var offset = y * source.Width + x;
                var missing = !float.IsFinite(source.DepthAt(offset)) || source.DepthAt(offset) <= 0;
                if (project.Settings.DepthSuppressSky && DepthSkyFilter.IsSky(semantics, (x + .5) / source.Width, (y + .5) / source.Height))
                {
                    continue;
                }

                if (!missing || SemanticProbabilities.ConservativeSample(semantics, semantics.RockSamples,
                    (double)x / source.Width, (double)y / source.Height) < .6)
                {
                    continue;
                }

                var anchor = CliffRecoverySearch.Anchor(source, x, y);
                if (anchor <= 0 || !CliffRecoverySearch.Supported(reference, verification, reference.Point(x, y, anchor), x, y))
                {
                    continue;
                }

                var norm = NormalizePatch(sampler, (double)x * fine.Width / source.Width, (double)y * fine.Height / source.Height, patch);
                if (!double.IsFinite(norm) || norm < .0008)
                {
                    continue;
                }

                var ray = (reference.Point(x, y, 1) - reference.Centre);
                for (var plane = 0; plane < hypotheses.Length; plane++)
                {
                    hypotheses[plane] = anchor * (.98 + .04 * plane / (hypotheses.Length - 1));
                }

                Interlocked.Increment(ref searched);
                var bestPlane = Search(views, reference.Centre, ray, patch, norm, hypotheses, costs);
                if (bestPlane < 0)
                {
                    continue;
                }

                var depth = hypotheses[bestPlane];
                var point = reference.Centre + ray * depth;
                if (!CliffRecoverySearch.Supported(reference, verification, point, x, y)
                    || !PhotometricSupport(reference, verification, views, point, ray, depth, x, y, patch, norm))
                {
                    continue;
                }

                result.Depth[offset] = (float)depth;
                result.Confidence[offset] = (float)Math.Min(.95, 1 - costs[bestPlane]);
                Interlocked.Increment(ref recovered);
            }
        });
        progress?.Invoke($"Recovered {recovered:N0}/{searched:N0} searched rock samples; all require two independent depth and photographic confirmations.");
        return new CliffRecoveryResult { Map = result, SearchedSamples = searched, RecoveredSamples = recovered, SourceImageWidth = fine.Width, Neighbours = views.Length };
    }

    private double NormalizePatch(GrayscaleSampler sampler, double x, double y, Span<double> patch)
    {
        var index = 0;
        double mean = 0;
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                patch[index] = sampler.Sample(x + dx, y + dy);
                mean += patch[index++];
            }
        }

        mean /= 25;
        double norm = 0;
        for (var sample = 0; sample < 25; sample++)
        {
            patch[sample] -= mean;
            norm += patch[sample] * patch[sample];
        }

        return norm;
    }

    private int Search(StereoView[] views, V3 centre, V3 ray, ReadOnlySpan<double> patch, double norm,
        ReadOnlySpan<double> hypotheses, Span<double> costs)
    {
        var best = -1;
        for (var plane = 0; plane < hypotheses.Length; plane += 2)
        {
            var other = Math.Min(plane + 1, hypotheses.Length - 1);
            var pair = StereoCostBatch.Calculate(views, centre, ray, hypotheses[plane], hypotheses[other], patch, norm);
            costs[plane] = pair.First;
            costs[other] = pair.Second;
        }

        for (var plane = 0; plane < costs.Length; plane++)
        {
            if (double.IsFinite(costs[plane]) && (best < 0 || costs[plane] < costs[best]))
            {
                best = plane;
            }
        }

        var unacceptable = best <= 0 || best >= hypotheses.Length - 1 || costs[best] >= .12;
        if (unacceptable)
        {
            return -1;
        }

        for (var plane = 0; plane < costs.Length; plane++)
        {
            if (costs[plane] - costs[best] < .015 && DistinctProjection(views, centre, ray, hypotheses[plane], hypotheses[best]))
            {
                return -1;
            }
        }

        return best;
    }

    private bool DistinctProjection(StereoView[] views, V3 centre, V3 ray, double first, double second)
    {
        foreach (var view in views)
        {
            var a = CameraGeometry.Rotate(view.WorldToCamera, centre + ray * first - view.Centre);
            var b = CameraGeometry.Rotate(view.WorldToCamera, centre + ray * second - view.Centre);
            if (a.Z <= 0 || b.Z <= 0)
            {
                continue;
            }

            var dx = (a.X / a.Z - b.X / b.Z) * view.Focal;
            var dy = (a.Y / a.Z - b.Y / b.Z) * view.Focal;
            if (dx * dx + dy * dy > 1)
            {
                return true;
            }
        }

        return false;
    }

    private bool PhotometricSupport(DepthView reference, DepthView[] verification, StereoView[] views,
        V3 point, V3 ray, double depth, int x, int y, ReadOnlySpan<double> patch, double norm)
    {
        var support = 0;
        for (var index = 0; index < views.Length; index++)
        {
            var view = verification[index];
            var valid = view.TrySurfacePoint(point, .005, out var surface) && reference.ReprojectsTo(surface, x, y, 1);
            if (!valid)
            {
                continue;
            }

            var cost = StereoCostBatch.Calculate(views.AsSpan(index, 1), reference.Centre, ray, depth, depth, patch, norm).First;
            if (cost < .15 && ++support >= 2)
            {
                return true;
            }
        }

        return false;
    }
}
