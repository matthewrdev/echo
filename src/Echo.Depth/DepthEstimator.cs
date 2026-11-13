using System.ComponentModel.Composition;
using System.Buffers;
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

    [ImportingConstructor]
    public DepthEstimator(ICameraGeometry cameraGeometry, IRasterImageService rasterImageService)
    {
        this.cameraGeometry = cameraGeometry;
        this.rasterImageService = rasterImageService;
    }

    public DepthMap Estimate(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        CameraPose camera,
        CancellationToken cancellation,
        int maxWorkers = 0,
        Action<string>? progress = null)
    {
        var sources = new Dictionary<string, RasterPixels>();
        DepthMap? prior = null;
        var width = Math.Min(160, project.Settings.DepthWidth);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke($"estimating {width}px depth level");
            prior = EstimateLevel(store, project, scene, camera, width, prior, sources, maxWorkers, cancellation);
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
        int levelWidth,
        DepthMap? prior,
        Dictionary<string, RasterPixels> sources,
        int maxWorkers,
        CancellationToken cancellation)
    {
        var photo = project.Images.Single(i => i.Id == camera.ImageId);
        var reference = ReadLevel(store, photo, levelWidth, sources);
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
        var depths = new List<double>();
        var projection = new CameraProjector(cameraGeometry, photo, camera);
        foreach (var point in scene.Points)
        {
            if (point.ImageA != photo.Id && point.ImageB != photo.Id)
            {
                continue;
            }
            var projected = projection.Project(new V3(point.Position));
            if (projected.HasValue && projected.Value.Z > 0)
            {
                depths.Add(projected.Value.Z);
            }
        }
        depths.Sort();
        var near = project.Settings.DepthNear;
        var far = project.Settings.DepthFar;
        if (depths.Count >= 10)
        {
            near = Math.Max(near, depths[depths.Count / 5] * .75);
            far = Math.Min(far, depths[depths.Count * 4 / 5] * 1.25);
            if (far <= near)
            {
                near = project.Settings.DepthNear;
                far = project.Settings.DepthFar;
            }
        }

        var focal = cameraGeometry.Focal(photo, camera) * width / photo.Width;
        var centre = new V3(camera.Centre);
        var median = depths.Count == 0 ? (near + far) * .5 : depths[depths.Count / 2];
        var minimumBaseline = Math.Clamp(median * .06, .75, 3);
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
            var otherPhoto = project.Images.Single(i => i.Id == other.ImageId);
            var resized = ReadLevel(store, otherPhoto, levelWidth, sources);
            var inverse = cameraGeometry.Transpose(other.Rotation);
            views.Add(new StereoView
            {
                Photo = otherPhoto,
                Camera = other,
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
        Parallel.For(3, height - 3, options,
            () => new DepthRowScratch(project.Settings.DepthRefinementSamples, prior == null ? planeDepths.Length : project.Settings.DepthRefinementSamples),
            (y, _, scratch) =>
        {
            Span<double> patch = stackalloc double[25];
            var hypothesisBuffer = scratch.Hypotheses;
            var costBuffer = scratch.Costs;
            for (var x = 3; x < width - 3; x++)
            {
                ReadOnlySpan<double> hypotheses = prior == null ? planeDepths
                    : hypothesisBuffer.Slice(0, CreateHypotheses(prior, x, y, width, height, near, far, hypothesisBuffer));
                if (hypotheses.Length == 0)
                {
                    continue;
                }

                var costs = costBuffer[..hypotheses.Length];
                cancellation.ThrowIfCancellationRequested();
                var norm = NormalizePatch(gray, width, x, y, patch);
                if (norm < .0008)
                {
                    continue;
                }

                var ray = cameraGeometry.Rotate(camera.Rotation, new V3((x - width * .5) / focal, (y - height * .5) / focal, 1));
                var best = double.PositiveInfinity;
                var bestPlane = -1;
                for (var plane = 0; plane < hypotheses.Length; plane++)
                {
                    var depth = hypotheses[plane];
                    var world = centre + ray * depth;
                    costs[plane] = CalculateCost(views, world, depth, patch, norm);
                    if (costs[plane] < best)
                    {
                        best = costs[plane];
                        bestPlane = plane;
                    }
                }

                if (bestPlane < 0 || best >= .18)
                {
                    continue;
                }

                var competitor = double.PositiveInfinity;
                for (var i = 0; i < costs.Length; i++)
                {
                    if (Math.Abs(i - bestPlane) > 2)
                    {
                        competitor = Math.Min(competitor, costs[i]);
                    }
                }

                if (prior == null && competitor - best < .015)
                {
                    continue;
                }

                var selectedDepth = InterpolateDepth(hypotheses, costs, bestPlane, best);
                result.Depth[y * width + x] = (float)selectedDepth;
                result.Confidence[y * width + x] = (float)(1 - best);
            }
            return scratch;
        }, scratch => scratch.Dispose());
        FilterUnsupportedSamples(result, cancellation);

        return result;
    }

    private GrayscaleImage ReadLevel(IProjectStore store, Photo photo, int width, Dictionary<string, RasterPixels> sources)
    {
        if (!sources.TryGetValue(photo.Id, out var source))
        {
            source = rasterImageService.ReadPixels(store.PathFor(photo.Source));
            sources.Add(photo.Id, source);
        }

        return rasterImageService.ResizeGray(source, width);
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

    private double CalculateCost(IReadOnlyList<StereoView> views, V3 world, double depth, ReadOnlySpan<double> patch, double norm)
    {
        var firstView = double.PositiveInfinity;
        var secondView = firstView;
        var count = 0;
        foreach (var view in views)
        {
            var p = cameraGeometry.Rotate(view.WorldToCamera, world - new V3(view.Camera.Centre));
            if (p.Z <= 0)
            {
                continue;
            }

            double sum = 0;
            double squared = 0;
            double dot = 0;
            var n = 0;
            var valid = true;
            // Warp a fronto-parallel reference patch into the neighbour at this depth.
            // This accounts for camera rotation and perspective within the patch.
            for (var dy = -2; dy <= 2 && valid; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    var q = p + view.PatchRight * (dx * depth) + view.PatchDown * (dy * depth);
                    var u = q.X / q.Z * view.Focal + view.Width * .5;
                    var v = q.Y / q.Z * view.Focal + view.Height * .5;
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
        return count < Math.Min(2, views.Count) ? double.PositiveInfinity : count == 1 ? firstView : (firstView + secondView) * .5;
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

    private void FilterUnsupportedSamples(DepthMap result, CancellationToken cancellation)
    {
        var width = result.Width;
        var height = result.Height;
        // Reject isolated samples and stabilise supported local patches without filling unseen pixels.
        var rawDepth = ArrayPool<float>.Shared.Rent(result.Depth.Length);
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
                    }
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rawDepth);
        }
    }
}
