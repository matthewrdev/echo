using System.ComponentModel.Composition;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Projects;

namespace Echo.Depth;

[Export(typeof(IDepthEstimator)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DepthEstimator : IDepthEstimator
{
    private readonly IRasterImageService rasterImageService;
    private readonly IDepthCostAccelerator[] accelerators;

    [ImportingConstructor]
    public DepthEstimator(IRasterImageService rasterImageService, [ImportMany] IEnumerable<IDepthCostAccelerator> accelerators)
    {
        this.rasterImageService = rasterImageService;
        this.accelerators = accelerators.ToArray();
    }

    public DepthMap Estimate(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        CameraPose camera,
        CancellationToken cancellation,
        int maxWorkers = 0,
        Action<string>? progress = null,
        Action<DepthLevelMetrics>? metrics = null,
        Action<ProcessingExecution>? execution = null)
    {
        var photo = FindPhoto(project.Images, camera.ImageId);
        var range = DepthRangeEstimator.Estimate(project, scene, photo, camera, cancellation);
        progress?.Invoke($"Depth range {range.Near:F2}–{range.Far:F2} ({range.Source}, {range.SampleCount:N0} scene samples; metres when georeferenced)");
        if (range.RejectedObservations > 0)
        {
            progress?.Invoke($"Depth range excluded {range.RejectedObservations:N0} sparse points with missing cameras, invalid geometry or less than 0.5° triangulation parallax");
        }
        var selected = DepthNeighbourSelector.Select(scene, camera, range.Median, project.Settings.DepthNeighbours, cancellation);
        var neighbours = new DepthNeighbour[selected.Length];
        for (var index = 0; index < selected.Length; index++)
        {
            var other = selected[index];
            neighbours[index] = new DepthNeighbour(other, FindPhoto(project.Images, other.ImageId), CameraGeometry.Transpose(other.Rotation));
        }
        using var sources = new DepthSourceImages(rasterImageService, store, neighbours.Length + 1);
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
            try
            {
                prior = EstimateLevel(project, photo, camera, neighbours, range, width, prior, sources, maxWorkers, cancellation, label, progress, metrics, execution);
            }
            finally
            {
                // Every row worker has finished before EstimateLevel returns or
                // throws. Only then may another image reuse these grayscale arrays.
                sources.ReleaseLevel();
            }
            if (width == project.Settings.DepthWidth)
            {
                return prior;
            }

            width = Math.Min(width * 2, project.Settings.DepthWidth);
        }
    }

    private Photo FindPhoto(List<Photo> photos, string imageId)
    {
        Photo? found = null;
        foreach (var photo in photos)
        {
            if (photo.Id != imageId)
            {
                continue;
            }
            if (found != null)
            {
                throw new EchoException($"Duplicate photograph ID: {imageId}.", 5);
            }
            found = photo;
        }
        return found ?? throw new EchoException($"Missing photograph: {imageId}.", 5);
    }

    private DepthMap EstimateLevel(ProjectData project,
        Photo photo,
        CameraPose camera,
        DepthNeighbour[] neighbours,
        DepthSearchRange range,
        int levelWidth,
        DepthMap? prior,
        DepthSourceImages sources,
        int maxWorkers,
        CancellationToken cancellation,
        string levelLabel,
        Action<string>? progress,
        Action<DepthLevelMetrics>? metrics,
        Action<ProcessingExecution>? execution)
    {
        var preparationStart = Stopwatch.GetTimestamp();
        var patchSpacing = Math.Min(photo.Width, levelWidth) >= 1280 ? 2 : 1;
        var margin = 2 * patchSpacing + 1;
        var reference = sources.ReadLevel(photo, levelWidth, patchSpacing > 1, cancellation);
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
        var focal = CameraGeometry.Focal(photo, camera) * width / photo.Width;
        var centre = new V3(camera.Centre);
        var patchRight = CameraGeometry.Rotate(camera.Rotation, new V3(patchSpacing / focal, 0, 0));
        var patchDown = CameraGeometry.Rotate(camera.Rotation, new V3(0, patchSpacing / focal, 0));
        var views = new StereoView[neighbours.Length];
        for (var index = 0; index < neighbours.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var other = neighbours[index].Camera;
            var otherPhoto = neighbours[index].Photo;
            var resized = sources.ReadLevel(otherPhoto, levelWidth, patchSpacing > 1, cancellation);
            var inverse = neighbours[index].WorldToCamera;
            views[index] = new StereoView
            {
                Centre = new V3(other.Centre),
                Focal = CameraGeometry.Focal(otherPhoto, other) * resized.Width / otherPhoto.Width,
                Width = resized.Width,
                Height = resized.Height,
                Gray = resized.Pixels,
                WorldToCamera = inverse,
                PatchRight = CameraGeometry.Rotate(inverse, patchRight),
                PatchDown = CameraGeometry.Rotate(inverse, patchDown)
            };
        }

        if (views.Length == 0)
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
        var description = $"{levelLabel}, {operation}, {views.Length} neighbours, up to {sampleCount} samples/pixel";
        progress?.Invoke(description);
        var levelProgress = new DepthLevelProgress(progress, description, height - 2 * margin);
        using var acceleration = OpenAcceleration(project.Settings.DepthBackend, views, cancellation);
        if (acceleration.Failure != null)
        {
            progress?.Invoke($"{levelLabel}: CPU fallback: {acceleration.Failure}");
        }
        var searchStart = Stopwatch.GetTimestamp();
        var globalSearches = 0;
        var recoveredSamples = 0;
        long acceleratedSearches = 0;
        long matchedSearches = 0;
        long cpuSearches = 0;
        long validatedSearches = 0;
        var validate = project.Settings.DepthBackend == "metal-validated";
        Parallel.For(margin, height - margin, options,
            () => new DepthRowScratch(project.Settings.DepthRefinementSamples, Math.Max(planeDepths.Length, project.Settings.DepthRefinementSamples)),
            (y, _, scratch) =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (acceleration.Session != null)
            {
                SearchAcceleratedRow(y, gray, width, height, patchSpacing, focal, camera, centre, views, prior, near, far,
                    planeDepths, scratch, result, acceleration, validate, cancellation);
                levelProgress.CompleteRow();
                return scratch;
            }
            Span<double> patch = stackalloc double[25];
            var hypothesisBuffer = scratch.Hypotheses;
            var costBuffer = scratch.Costs;
            var rayY = (y - height * .5) / focal;
            for (var x = margin; x < width - margin; x++)
            {
                cancellation.ThrowIfCancellationRequested();
                var norm = NormalizePatch(gray, width, x, y, patch, patchSpacing);
                if (norm < .0008)
                {
                    continue;
                }

                var ray = CameraGeometry.Rotate(camera.Rotation, new V3((x - width * .5) / focal, rayY, 1));
                var hypotheses = hypothesisBuffer[..(prior == null ? 0 : CreateHypotheses(prior, x, y, width, height, near, far, hypothesisBuffer))];
                var selectedDepth = 0.0;
                var best = double.PositiveInfinity;
                scratch.CpuSearches += hypotheses.Length > 0 ? 1 : 0;
                var found = hypotheses.Length > 0 && SearchDepth(views, centre, ray,
                    patch, norm, hypotheses, costBuffer, false, cancellation, out selectedDepth, out best);
                if (!found)
                {
                    // A missing or rejected coarse estimate is not evidence of an empty
                    // surface. Search the full interval again with this level's image detail.
                    scratch.GlobalSearches++;
                    scratch.CpuSearches++;
                    found = SearchDepth(views, centre, ray, patch, norm,
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
            Interlocked.Add(ref acceleratedSearches, scratch.AcceleratedSearches);
            Interlocked.Add(ref matchedSearches, scratch.MatchedSearches);
            Interlocked.Add(ref cpuSearches, scratch.CpuSearches);
            Interlocked.Add(ref validatedSearches, scratch.ValidatedSearches);
            scratch.Dispose();
        });
        var filterStart = Stopwatch.GetTimestamp();
        progress?.Invoke($"{levelLabel}: filtering unsupported depth samples");
        var retainedSamples = FilterUnsupportedSamples(result, cancellation);
        var actualBackend = acceleratedSearches == 0 ? "cpu" : cpuSearches > 0 ? "metal+cpu" : "metal";
        var precision = acceleratedSearches == 0 ? "float64 with float32 samples/output"
            : validate ? "Metal float32 candidates; CPU float64 reference with float32 samples/output"
            : "Metal float32 costs; CPU float64 selection/interpolation; float32 samples/output";
        var validation = acceleratedSearches == 0 ? "none"
            : validate ? "every search: acceptance and bitwise float32 depth/confidence; CPU correction on mismatch"
            : "cost shape/range checks; no CPU reference comparison";
        execution?.Invoke(new ProcessingExecution
        {
            Component = "stereo-depth-search",
            RequestedBackend = project.Settings.DepthBackend,
            ActualBackend = actualBackend,
            Implementation = acceleratedSearches > 0 ? acceleration.Session!.Implementation : "echo-cpu-stereo-cost; Vector128<double> when available",
            Device = acceleration.Session?.Device,
            Precision = precision,
            Validation = validation,
            OutputAuthority = validate || acceleratedSearches == 0 ? "cpu-reference" : actualBackend,
            CpuImplementation = cpuSearches == 0 ? null : Vector128.IsHardwareAccelerated ? "Vector128<double>" : "scalar",
            FallbackReason = acceleration.Failure ?? (validatedSearches > matchedSearches ? "Nonidentical Metal search results were replaced with the CPU reference." : null),
            LevelWidth = width,
            Workers = options.MaxDegreeOfParallelism,
            AcceleratedSearches = acceleratedSearches,
            MatchedSearches = matchedSearches,
            CpuSearches = cpuSearches,
            ValidatedSearches = validatedSearches,
            CorrectedSearches = validatedSearches - matchedSearches
        });
        progress?.Invoke($"{levelLabel}: {retainedSamples:N0}/{result.Depth.Length:N0} depth samples retained ({100.0 * retainedSamples / result.Depth.Length:F2}%); {globalSearches:N0} full-range searches, {recoveredSamples:N0} recovery candidates before filtering");
        metrics?.Invoke(new DepthLevelMetrics(photo.Id, width, height, views.Length,
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

    internal bool SearchDepth(ReadOnlySpan<StereoView> views, V3 centre, V3 ray, ReadOnlySpan<double> patch,
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
            if (Vector128.IsHardwareAccelerated && plane + 1 < hypotheses.Length)
            {
                var pair = StereoCostBatch.Calculate(views, centre, ray, depth, hypotheses[plane + 1], patch, norm);
                costs[plane] = pair.First;
                costs[plane + 1] = pair.Second;
                if (costs[plane] < best)
                {
                    best = costs[plane];
                    bestPlane = plane;
                }
                plane++;
            }
            else
            {
                costs[plane] = CalculateCost(views, centre + ray * depth, depth, patch, norm);
            }
            if (costs[plane] < best)
            {
                best = costs[plane];
                bestPlane = plane;
            }
        }
        return AcceptDepth(views, centre, ray, hypotheses, costs, checkAmbiguity, bestPlane, best, out selectedDepth);
    }

    private bool SelectDepth(ReadOnlySpan<StereoView> views, V3 centre, V3 ray, ReadOnlySpan<double> hypotheses,
        ReadOnlySpan<double> costs, bool checkAmbiguity, out double selectedDepth, out double best)
    {
        best = double.PositiveInfinity;
        var bestPlane = -1;
        for (var plane = 0; plane < costs.Length; plane++)
        {
            if (costs[plane] < best)
            {
                best = costs[plane];
                bestPlane = plane;
            }
        }
        return AcceptDepth(views, centre, ray, hypotheses, costs, checkAmbiguity, bestPlane, best, out selectedDepth);
    }

    private bool AcceptDepth(ReadOnlySpan<StereoView> views, V3 centre, V3 ray, ReadOnlySpan<double> hypotheses,
        ReadOnlySpan<double> costs, bool checkAmbiguity, int bestPlane, double best, out double selectedDepth)
    {
        selectedDepth = 0;
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

    private DepthAccelerationState OpenAcceleration(string backend, StereoView[] views, CancellationToken cancellation)
    {
        if (backend == "cpu")
        {
            return new DepthAccelerationState(null, null);
        }
        if (backend is not ("auto" or "metal" or "metal-validated"))
        {
            throw new EchoException("Unknown depth backend: " + backend, 2);
        }
        var accelerator = accelerators.SingleOrDefault(candidate => candidate.Backend == "metal");
        if (accelerator == null)
        {
            return new DepthAccelerationState(null, "Metal module is not installed.");
        }
        try
        {
            return new DepthAccelerationState(accelerator.Open(views, cancellation), null);
        }
        catch (DepthAccelerationException error)
        {
            return new DepthAccelerationState(null, error.Message);
        }
    }

    private void SearchAcceleratedRow(int y, float[] gray, int width, int height, int patchSpacing, double focal,
        CameraPose camera, V3 centre, StereoView[] views, DepthMap? prior, double near, double far,
        double[] planeDepths, DepthRowScratch scratch, DepthMap result, DepthAccelerationState acceleration,
        bool validate, CancellationToken cancellation)
    {
        var local = new List<DepthSearchSample>();
        var global = new List<DepthSearchSample>();
        Span<double> patch = stackalloc double[25];
        var rayY = (y - height * .5) / focal;
        var margin = 2 * patchSpacing + 1;
        for (var x = margin; x < width - margin; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            var norm = NormalizePatch(gray, width, x, y, patch, patchSpacing);
            if (norm < .0008)
            {
                continue;
            }
            var count = prior == null ? 0 : CreateHypotheses(prior, x, y, width, height, near, far, scratch.Hypotheses);
            var sample = new DepthSearchSample
            {
                PixelIndex = y * width + x,
                Ray = CameraGeometry.Rotate(camera.Rotation, new V3((x - width * .5) / focal, rayY, 1)),
                Patch = patch.ToArray(),
                Norm = norm,
                Hypotheses = count > 0 ? scratch.Hypotheses[..count].ToArray() : planeDepths
            };
            (count > 0 ? local : global).Add(sample);
        }
        var rejected = SearchAcceleratedBatch(local, false, views, centre, result, scratch, acceleration, validate, cancellation);
        foreach (var sample in rejected)
        {
            sample.Hypotheses = planeDepths;
            global.Add(sample);
        }
        scratch.GlobalSearches += global.Count;
        var missing = SearchAcceleratedBatch(global, true, views, centre, result, scratch, acceleration, validate, cancellation);
        if (prior != null)
        {
            scratch.RecoveredSamples += global.Count - missing.Count;
        }
    }

    private List<DepthSearchSample> SearchAcceleratedBatch(List<DepthSearchSample> samples, bool checkAmbiguity,
        StereoView[] views, V3 centre, DepthMap result, DepthRowScratch scratch, DepthAccelerationState acceleration,
        bool validate, CancellationToken cancellation)
    {
        var rejected = new List<DepthSearchSample>();
        if (samples.Count == 0)
        {
            return rejected;
        }
        var acceleratedCosts = CalculateAcceleratedCosts(centre, samples, acceleration, cancellation);
        for (var index = 0; index < samples.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = samples[index];
            bool found;
            double depth;
            double cost;
            if (acceleratedCosts == null)
            {
                scratch.CpuSearches++;
                found = SearchDepth(views, centre, sample.Ray, sample.Patch, sample.Norm, sample.Hypotheses,
                    scratch.Costs, checkAmbiguity, cancellation, out depth, out cost);
            }
            else
            {
                var costs = scratch.Costs[..sample.Hypotheses.Length];
                for (var plane = 0; plane < costs.Length; plane++)
                {
                    costs[plane] = acceleratedCosts[index * costs.Length + plane];
                }
                found = SelectDepth(views, centre, sample.Ray, sample.Hypotheses, costs,
                    checkAmbiguity, out depth, out cost);
                scratch.AcceleratedSearches++;
                if (validate)
                {
                    var cpuFound = SearchDepth(views, centre, sample.Ray, sample.Patch, sample.Norm, sample.Hypotheses,
                        scratch.Costs, checkAmbiguity, cancellation, out var cpuDepth, out var cpuCost);
                    scratch.CpuSearches++;
                    scratch.ValidatedSearches++;
                    var sameValues = BitConverter.SingleToInt32Bits((float)depth) == BitConverter.SingleToInt32Bits((float)cpuDepth)
                        && BitConverter.SingleToInt32Bits((float)(1 - cost)) == BitConverter.SingleToInt32Bits((float)(1 - cpuCost));
                    if (found == cpuFound && (!found || sameValues))
                    {
                        scratch.MatchedSearches++;
                    }
                    found = cpuFound;
                    depth = cpuDepth;
                    cost = cpuCost;
                }
            }
            if (!found)
            {
                rejected.Add(sample);
                continue;
            }
            result.Depth[sample.PixelIndex] = (float)depth;
            result.Confidence[sample.PixelIndex] = (float)(1 - cost);
        }
        return rejected;
    }

    private float[]? CalculateAcceleratedCosts(V3 centre, List<DepthSearchSample> samples,
        DepthAccelerationState acceleration, CancellationToken cancellation)
    {
        if (acceleration.Failure != null)
        {
            return null;
        }
        try
        {
            var costs = acceleration.Session!.Calculate(centre, samples, cancellation);
            if (costs.Length != checked(samples.Count * samples[0].Hypotheses.Length))
            {
                throw new DepthAccelerationException("Metal returned an invalid cost count.");
            }
            foreach (var cost in costs)
            {
                // Positive infinity means insufficient support, as on the CPU.
                var invalidCost = float.IsNaN(cost) || cost < 0 || cost > 2 && !float.IsPositiveInfinity(cost);
                if (invalidCost)
                {
                    throw new DepthAccelerationException("Metal returned an invalid stereo cost.");
                }
            }
            return costs;
        }
        catch (DepthAccelerationException error)
        {
            acceleration.Fail(error.Message);
            return null;
        }
    }

    internal bool DistinctProjection(ReadOnlySpan<StereoView> views, V3 centre, V3 ray, double firstDepth, double secondDepth)
    {
        var firstPoint = centre + ray * firstDepth;
        var secondPoint = centre + ray * secondDepth;
        foreach (var view in views)
        {
            var first = CameraGeometry.Rotate(view.WorldToCamera, firstPoint - view.Centre);
            var second = CameraGeometry.Rotate(view.WorldToCamera, secondPoint - view.Centre);
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
        var inverseNear = 1 / localNear;
        var inverseSpan = 1 / localFar - inverseNear;
        for (var p = 0; p < hypotheses.Length; p++)
        {
            hypotheses[p] = 1 / (inverseNear + inverseSpan * p / (hypotheses.Length - 1));
        }

        return hypotheses.Length;
    }

    internal double NormalizePatch(float[] gray, int width, int x, int y, Span<double> patch, int spacing = 1)
    {
        var n = 0;
        double mean = 0;
        for (var dy = -2; dy <= 2; dy++)
        {
            var row = (y + dy * spacing) * width;
            for (var dx = -2; dx <= 2; dx++)
            {
                patch[n] = gray[row + x + dx * spacing];
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
        Span<V3> columns = stackalloc V3[5];
        foreach (var view in views)
        {
            var p = CameraGeometry.Rotate(view.WorldToCamera, world - view.Centre);
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
            var sampler = new GrayscaleSampler(view.Gray, view.Width, view.Height);
            for (var column = 0; column < columns.Length; column++)
            {
                columns[column] = p + view.PatchRight * ((column - 2) * depth);
            }
            // Warp a fronto-parallel reference patch into the neighbour at this depth.
            // This accounts for camera rotation and perspective within the patch.
            for (var dy = -2; dy <= 2 && valid; dy++)
            {
                var down = view.PatchDown * (dy * depth);
                for (var dx = -2; dx <= 2; dx++)
                {
                    var q = columns[dx + 2] + down;
                    var u = q.X / q.Z * focal + halfWidth;
                    var v = q.Y / q.Z * focal + halfHeight;
                    var sample = q.Z > 0 ? sampler.Sample(u, v) : float.NaN;
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

    internal double InterpolateDepth(ReadOnlySpan<double> hypotheses, ReadOnlySpan<double> costs, int bestPlane, double best)
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
