using System.ComponentModel.Composition;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Projects;

namespace Echo.Depth;

[Export(typeof(IDepthEstimator)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DepthEstimator : IDepthEstimator
{
    private readonly ICameraGeometry cameraGeometry;
    private readonly IRasterImageService rasterImageService;
    private readonly IDepthRangeEstimator rangeEstimator;

    [ImportingConstructor]
    public DepthEstimator(ICameraGeometry cameraGeometry, IRasterImageService rasterImageService, IDepthRangeEstimator rangeEstimator)
    {
        this.cameraGeometry = cameraGeometry;
        this.rasterImageService = rasterImageService;
        this.rangeEstimator = rangeEstimator;
    }

    public DepthMap Estimate(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        CameraPose camera,
        CancellationToken cancellation,
        int maxWorkers = 0,
        Action<string>? progress = null,
        Action<DepthLevelMetrics>? metrics = null)
    {
        var photo = project.Images.Single(image => image.Id == camera.ImageId);
        var range = rangeEstimator.Estimate(project, scene, photo, camera, cancellation);
        progress?.Invoke($"Depth range {range.Near:F2}–{range.Far:F2} ({range.Source}, {range.SampleCount:N0} scene samples; metres when georeferenced)");
        using var sources = new DepthSourceImages(rasterImageService, store);
        DepthMap? prior = null;
        var width = Math.Min(160, project.Settings.DepthWidth);
        var levelCount = 1;
        for (var levelWidth = width; levelWidth < project.Settings.DepthWidth; levelWidth = Math.Min(levelWidth * 2, project.Settings.DepthWidth))
        {
            levelCount++;
        }
        var level = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var label = $"level {++level}/{levelCount}, {width}px";
            progress?.Invoke($"{label}: loading reference and neighbouring images");
            prior = EstimateLevel(store, project, scene, camera, range, width, prior, sources, maxWorkers, cancellation, label, progress, metrics);
            if (width == project.Settings.DepthWidth)
            {
                return prior;
            }

            width = Math.Min(width * 2, project.Settings.DepthWidth);
        }
    }

    private DepthMap EstimateLevel(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        CameraPose camera,
        DepthSearchRange range,
        int levelWidth,
        DepthMap? prior,
        DepthSourceImages sources,
        int maxWorkers,
        CancellationToken cancellation,
        string levelLabel,
        Action<string>? progress,
        Action<DepthLevelMetrics>? metrics)
    {
        var preparationStart = Stopwatch.GetTimestamp();
        var photo = project.Images.Single(i => i.Id == camera.ImageId);
        var reference = sources.ReadLevel(photo, levelWidth);
        var gray = reference.Pixels;
        var width = reference.Width;
        var height = reference.Height;
        var result = new DepthMap
        {
            ImageId = photo.Id,
            Width = width,
            Height = height,
            Depth = new float[width * height],
            Confidence = new float[width * height]
        };
        var near = range.Near;
        var far = range.Far;
        var focal = cameraGeometry.Focal(photo, camera) * width / photo.Width;
        var centre = new V3(camera.Centre);
        var minimumBaseline = Math.Clamp(range.Median * .06, .75, 3);
        var forward = cameraGeometry.Rotate(camera.Rotation, new V3(0, 0, 1));
        var candidates = scene.Cameras.Where(candidate =>
        {
            var isDifferentImage = candidate.ImageId != camera.ImageId;
            var hasBaseline = (new V3(candidate.Centre) - centre).Length > .05;
            var facing = cameraGeometry.Rotate(candidate.Rotation, new V3(0, 0, 1));
            var hasSimilarOrientation = facing.Dot(forward) > .8;
            return isDifferentImage && hasBaseline && hasSimilarOrientation;
        }).ToList();
        var neighbours = candidates.Where(c => (new V3(c.Centre) - centre).Length >= minimumBaseline).ToList();
        if (neighbours.Count < 2)
        {
            neighbours = candidates;
        }

        var views = new List<StereoView>();
        foreach (var other in neighbours.OrderBy(c => (new V3(c.Centre) - centre).Length).Take(project.Settings.DepthNeighbours))
        {
            cancellation.ThrowIfCancellationRequested();
            var otherPhoto = project.Images.Single(i => i.Id == other.ImageId);
            var resized = sources.ReadLevel(otherPhoto, levelWidth);
            var inverse = cameraGeometry.Transpose(other.Rotation);
            views.Add(new StereoView
            {
                Centre = new V3(other.Centre),
                Focal = cameraGeometry.Focal(otherPhoto, other) * resized.Width / otherPhoto.Width,
                Width = resized.Width,
                Height = resized.Height,
                Gray = resized.Pixels,
                WorldToCamera = inverse,
                PatchRight = cameraGeometry.Rotate(inverse, cameraGeometry.Rotate(camera.Rotation, new V3(1 / focal, 0, 0))),
                PatchDown = cameraGeometry.Rotate(inverse, cameraGeometry.Rotate(camera.Rotation, new V3(0, 1 / focal, 0)))
            });
        }

        if (views.Count == 0)
        {
            throw new EchoException($"No suitable stereo neighbour for {photo.Name}.", 5);
        }

        var planeDepths = new double[project.Settings.DepthPlanes];
        for (var plane = 0; plane < planeDepths.Length; plane++)
        {
            planeDepths[plane] = 1 / (1 / near + (1 / far - 1 / near) * plane / (project.Settings.DepthPlanes - 1));
        }
        var options = new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = maxWorkers > 0 ? maxWorkers : Math.Max(1, Environment.ProcessorCount / 2)
        };
        var operation = prior == null ? "initial depth search" : "depth refinement and missing-depth search";
        var sampleCount = planeDepths.Length + (prior == null ? 0 : project.Settings.DepthRefinementSamples);
        var description = $"{levelLabel}, {operation}, {views.Count} neighbours, up to {sampleCount} samples/pixel";
        progress?.Invoke(description);
        var levelProgress = new DepthLevelProgress(progress, description, height - 6);
        var searchStart = Stopwatch.GetTimestamp();
        var globalSearches = 0;
        var recoveredSamples = 0;
        Parallel.For(3, height - 3, options,
            () => new DepthRowScratch(project.Settings.DepthRefinementSamples, Math.Max(planeDepths.Length, project.Settings.DepthRefinementSamples)),
            (y, _, scratch) =>
        {
            cancellation.ThrowIfCancellationRequested();
            Span<double> patch = stackalloc double[25];
            var hypothesisBuffer = scratch.Hypotheses;
            var costBuffer = scratch.Costs;
            for (var x = 3; x < width - 3; x++)
            {
                cancellation.ThrowIfCancellationRequested();
                var norm = NormalizePatch(gray, width, x, y, patch);
                if (norm < .0008)
                {
                    continue;
                }

                var ray = cameraGeometry.Rotate(camera.Rotation, new V3((x - width * .5) / focal, (y - height * .5) / focal, 1));
                var hypotheses = hypothesisBuffer[..(prior == null ? 0 : CreateHypotheses(prior, x, y, width, height, near, far, hypothesisBuffer))];
                var selectedDepth = 0.0;
                var best = double.PositiveInfinity;
                var found = hypotheses.Length > 0 && SearchDepth(CollectionsMarshal.AsSpan(views), centre, ray,
                    patch, norm, hypotheses, costBuffer, false, cancellation, out selectedDepth, out best);
                if (!found)
                {
                    // A missing or rejected coarse estimate is not evidence of an empty
                    // surface. Search the full interval again with this level's image detail.
                    scratch.GlobalSearches++;
                    found = SearchDepth(CollectionsMarshal.AsSpan(views), centre, ray, patch, norm,
                        planeDepths, costBuffer, true, cancellation, out selectedDepth, out best);
                    if (found && prior != null)
                    {
                        scratch.RecoveredSamples++;
                    }
                }
                if (!found)
                {
                    continue;
                }
                result.Depth[y * width + x] = (float)selectedDepth;
                result.Confidence[y * width + x] = (float)(1 - best);
            }
            levelProgress.CompleteRow();
            return scratch;
        }, scratch =>
        {
            Interlocked.Add(ref globalSearches, scratch.GlobalSearches);
            Interlocked.Add(ref recoveredSamples, scratch.RecoveredSamples);
            scratch.Dispose();
        });
        var filterStart = Stopwatch.GetTimestamp();
        progress?.Invoke($"{levelLabel}: filtering unsupported depth samples");
        var retainedSamples = FilterUnsupportedSamples(result, cancellation);
        progress?.Invoke($"{levelLabel}: {retainedSamples:N0}/{result.Depth.Length:N0} depth samples retained ({100.0 * retainedSamples / result.Depth.Length:F2}%); {globalSearches:N0} full-range searches, {recoveredSamples:N0} recovery candidates before filtering");
        metrics?.Invoke(new DepthLevelMetrics(photo.Id, width, height, views.Count,
            Stopwatch.GetElapsedTime(preparationStart, searchStart).TotalSeconds,
            Stopwatch.GetElapsedTime(searchStart, filterStart).TotalSeconds,
            Stopwatch.GetElapsedTime(filterStart).TotalSeconds)
        {
            GlobalSearches = globalSearches,
            RecoveredSamples = recoveredSamples,
            RetainedSamples = retainedSamples,
            SearchNear = range.Near,
            SearchFar = range.Far,
            RangeSource = range.Source
        });

        return result;
    }

    private bool SearchDepth(ReadOnlySpan<StereoView> views, V3 centre, V3 ray, ReadOnlySpan<double> patch,
        double norm, ReadOnlySpan<double> hypotheses, Span<double> costBuffer, bool checkAmbiguity,
        CancellationToken cancellation, out double selectedDepth, out double best)
    {
        selectedDepth = 0;
        best = double.PositiveInfinity;
        var costs = costBuffer[..hypotheses.Length];
        var bestPlane = -1;
        for (var plane = 0; plane < hypotheses.Length; plane++)
        {
            if ((plane & 31) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            var depth = hypotheses[plane];
            costs[plane] = CalculateCost(views, centre + ray * depth, depth, patch, norm);
            if (costs[plane] < best)
            {
                best = costs[plane];
                bestPlane = plane;
            }
        }
        if (bestPlane < 0 || best >= .18)
        {
            return false;
        }
        if (checkAmbiguity)
        {
            for (var plane = 0; plane < costs.Length; plane++)
            {
                var competingCost = plane != bestPlane && costs[plane] - best < .015;
                if (competingCost && DistinctProjection(views, centre, ray, hypotheses[bestPlane], hypotheses[plane]))
                {
                    return false;
                }
            }
        }
        selectedDepth = InterpolateDepth(hypotheses, costs, bestPlane, best);
        return true;
    }

    private bool DistinctProjection(ReadOnlySpan<StereoView> views, V3 centre, V3 ray, double firstDepth, double secondDepth)
    {
        var firstPoint = centre + ray * firstDepth;
        var secondPoint = centre + ray * secondDepth;
        foreach (var view in views)
        {
            var first = cameraGeometry.Rotate(view.WorldToCamera, firstPoint - view.Centre);
            var second = cameraGeometry.Rotate(view.WorldToCamera, secondPoint - view.Centre);
            if (first.Z <= 0 || second.Z <= 0)
            {
                continue;
            }
            var dx = view.Focal * (first.X / first.Z - second.X / second.Z);
            var dy = view.Focal * (first.Y / first.Z - second.Y / second.Z);
            // Nearby samples of the same photometric minimum are not competing
            // surfaces. Compare hypotheses separated by at least one image pixel,
            // rather than by a fixed number of inverse-depth array entries.
            if (dx * dx + dy * dy >= 1)
            {
                return true;
            }
        }
        return false;
    }

    private int CreateHypotheses(DepthMap prior, int x, int y, int width, int height,
        double near, double far, Span<double> hypotheses)
    {
        var px = x * prior.Width / width;
        var py = y * prior.Height / height;
        var count = 0;
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        for (var sy = Math.Max(0, py - 1); sy <= Math.Min(prior.Height - 1, py + 1); sy++)
        {
            var row = prior.Depth.AsSpan(sy * prior.Width, prior.Width);
            for (var sx = Math.Max(0, px - 1); sx <= Math.Min(prior.Width - 1, px + 1); sx++)
            {
                var depth = row[sx];
                if (depth > 0)
                {
                    count++;
                    minimum = Math.Min(minimum, depth);
                    maximum = Math.Max(maximum, depth);
                }
            }
        }

        if (count < 2)
        {
            return 0;
        }

        var localNear = Math.Max(near, minimum * .98);
        var localFar = Math.Min(far, maximum * 1.02);
        for (var p = 0; p < hypotheses.Length; p++)
        {
            hypotheses[p] = 1 / (1 / localNear + (1 / localFar - 1 / localNear) * p / (hypotheses.Length - 1));
        }

        return hypotheses.Length;
    }

    private double NormalizePatch(float[] gray, int width, int x, int y, Span<double> patch)
    {
        var n = 0;
        double mean = 0;
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                patch[n] = gray[(y + dy) * width + x + dx];
                mean += patch[n++];
            }
        }

        mean /= 25;
        double norm = 0;
        for (var i = 0; i < 25; i++)
        {
            patch[i] -= mean;
            norm += patch[i] * patch[i];
        }
        return norm;
    }

    private double CalculateCost(ReadOnlySpan<StereoView> views, V3 world, double depth, ReadOnlySpan<double> patch, double norm)
    {
        var firstView = double.PositiveInfinity;
        var secondView = firstView;
        var count = 0;
        foreach (var view in views)
        {
            var p = cameraGeometry.Rotate(view.WorldToCamera, world - view.Centre);
            if (p.Z <= 0)
            {
                continue;
            }

            double sum = 0;
            double squared = 0;
            double dot = 0;
            var n = 0;
            var valid = true;
            var focal = view.Focal;
            var halfWidth = view.Width * .5;
            var halfHeight = view.Height * .5;
            // Warp a fronto-parallel reference patch into the neighbour at this depth.
            // This accounts for camera rotation and perspective within the patch.
            for (var dy = -2; dy <= 2 && valid; dy++)
            {
                var down = view.PatchDown * (dy * depth);
                for (var dx = -2; dx <= 2; dx++)
                {
                    var q = p + view.PatchRight * (dx * depth) + down;
                    var u = q.X / q.Z * focal + halfWidth;
                    var v = q.Y / q.Z * focal + halfHeight;
                    var sample = q.Z > 0 ? rasterImageService.Sample(view.Gray, view.Width, view.Height, u, v) : float.NaN;
                    if (!float.IsFinite(sample))
                    {
                        valid = false;
                        break;
                    }

                    sum += sample;
                    squared += sample * sample;
                    dot += patch[n++] * sample;
                }
            }

            var variance = squared - sum * sum / 25;
            if (!valid || variance < .0002)
            {
                continue;
            }

            var cost = 1 - Math.Clamp(dot / Math.Sqrt(norm * variance), -1, 1);
            count++;
            if (cost < firstView)
            {
                secondView = firstView;
                firstView = cost;
            }
            else if (cost < secondView)
            {
                secondView = cost;
            }
        }

        // At least two supporting views where available; tolerate one occluded neighbour.
        return count < Math.Min(2, views.Length) ? double.PositiveInfinity : count == 1 ? firstView : (firstView + secondView) * .5;
    }

    private double InterpolateDepth(ReadOnlySpan<double> hypotheses, ReadOnlySpan<double> costs, int bestPlane, double best)
    {
        var selectedDepth = hypotheses[bestPlane];
        var hasFiniteNeighbours = bestPlane > 0
            && bestPlane + 1 < costs.Length
            && double.IsFinite(costs[bestPlane - 1])
            && double.IsFinite(costs[bestPlane + 1]);
        if (hasFiniteNeighbours)
        {
            var curve = costs[bestPlane - 1] - 2 * best + costs[bestPlane + 1];
            if (curve > 1e-8)
            {
                var offset = Math.Clamp(.5 * (costs[bestPlane - 1] - costs[bestPlane + 1]) / curve, -.5, .5);
                var inverseStep = 1 / hypotheses[1] - 1 / hypotheses[0];
                selectedDepth = 1 / (1 / selectedDepth + offset * inverseStep);
            }
        }
        return selectedDepth;
    }

    private int FilterUnsupportedSamples(DepthMap result, CancellationToken cancellation)
    {
        var width = result.Width;
        var height = result.Height;
        // Reject isolated samples and stabilise supported local patches without filling unseen pixels.
        var rawDepth = ArrayPool<float>.Shared.Rent(result.Depth.Length);
        var retained = 0;
        try
        {
            result.Depth.AsSpan().CopyTo(rawDepth);
            Span<float> support = stackalloc float[9];
            for (var y = 1; y < height - 1; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var x = 1; x < width - 1; x++)
                {
                    var index = y * width + x;
                    var depth = rawDepth[index];
                    if (depth <= 0)
                    {
                        continue;
                    }

                    var supportCount = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var other = rawDepth[(y + dy) * width + x + dx];
                            if (other > 0 && Math.Abs(other - depth) < depth * .05)
                            {
                                support[supportCount++] = other;
                            }
                        }
                    }

                    if (supportCount < 4)
                    {
                        result.Depth[index] = 0;
                        result.Confidence[index] = 0;
                    }
                    else
                    {
                        var neighbours = support[..supportCount];
                        neighbours.Sort();
                        result.Depth[index] = neighbours[supportCount / 2];
                        retained++;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rawDepth);
        }
        return retained;
    }
}
