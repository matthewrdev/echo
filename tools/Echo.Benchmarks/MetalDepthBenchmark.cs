using System.Diagnostics;
using System.Runtime.InteropServices;
using Echo.Contracts;
using Echo.Depth;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Benchmarks;

// Experimental fixture export/validation only. The processing pipeline never selects Metal.
public sealed class MetalDepthBenchmark
{
    private readonly IDepthEstimator estimator;
    private readonly IRasterImageService images;
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;

    public MetalDepthBenchmark(IDepthEstimator estimator, IRasterImageService images, IProjectStoreFactory stores, IJsonCodec json)
    {
        this.estimator = estimator;
        this.images = images;
        this.stores = stores;
        this.json = json;
    }

    public int Run(string[] args)
    {
        if (args.Length is < 6 or > 7)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks metal-depth export|verify PROJECT RUN WIDTH DIRECTORY STRIDE [CAMERA_INDEX]");
            return 2;
        }
        var store = stores.Open(args[1]);
        var run = store.GetRun(args[2]);
        var project = run.Snapshot;
        var scene = json.Read<ReconstructionScene>(store.PathFor(run.Artifacts["alignment"] + "/scene.json"));
        var camera = scene.Cameras[args.Length == 7 ? int.Parse(args[6]) : 0];
        var photo = project.Images.Single(image => image.Id == camera.ImageId);
        var width = int.Parse(args[3]);
        var directory = Path.GetFullPath(args[4]);
        var stride = Math.Max(1, int.Parse(args[5]));
        var range = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
        var selected = DepthNeighbourSelector.Select(project, scene, camera, range, project.Settings.DepthNeighbours, default);
        var implementation = (DepthEstimator)estimator;
        using var sources = new DepthSourceImages(images, store, selected.Length + 1);
        var reference = sources.ReadLevel(photo, width);
        var focal = CameraGeometry.Focal(photo, camera) * reference.Width / photo.Width;
        var centre = new V3(camera.Centre);
        var right = CameraGeometry.Rotate(camera.Rotation, new V3(1 / focal, 0, 0));
        var down = CameraGeometry.Rotate(camera.Rotation, new V3(0, 1 / focal, 0));
        var views = selected.Select(other =>
        {
            var otherPhoto = project.Images.Single(image => image.Id == other.ImageId);
            var gray = sources.ReadLevel(otherPhoto, width);
            var inverse = CameraGeometry.Transpose(other.Rotation);
            return new StereoView
            {
                Centre = new V3(other.Centre),
                Focal = CameraGeometry.Focal(otherPhoto, other) * gray.Width / otherPhoto.Width,
                Width = gray.Width,
                Height = gray.Height,
                Gray = gray.Pixels,
                WorldToCamera = inverse,
                PatchRight = CameraGeometry.Rotate(inverse, right),
                PatchDown = CameraGeometry.Rotate(inverse, down)
            };
        }).ToArray();
        var samples = new List<MetalDepthSample>();
        for (var y = 3; y < reference.Height - 3; y += stride)
        {
            for (var x = 3; x < reference.Width - 3; x += stride)
            {
                var patch = new double[25];
                var norm = implementation.NormalizePatch(reference.Pixels, reference.Width, x, y, patch);
                if (norm >= .0008)
                {
                    var ray = CameraGeometry.Rotate(camera.Rotation, new V3((x - reference.Width * .5) / focal, (y - reference.Height * .5) / focal, 1));
                    samples.Add(new MetalDepthSample(ray, patch, norm));
                }
            }
        }
        var depths = Enumerable.Range(0, project.Settings.DepthPlanes)
            .Select(plane => 1 / (1 / range.Near + (1 / range.Far - 1 / range.Near) * plane / (project.Settings.DepthPlanes - 1))).ToArray();
        Directory.CreateDirectory(directory);
        if (args[0] == "export")
        {
            Export(directory, reference.Width, reference.Height, photo.Id, centre, views, samples, depths, implementation);
        }
        else if (args[0] == "verify")
        {
            Verify(directory, centre, views, samples, depths, implementation);
        }
        else
        {
            throw new ArgumentException("Choose export or verify.");
        }
        return 0;
    }

    private void Export(string directory, int width, int height, string imageId, V3 centre,
        StereoView[] views, List<MetalDepthSample> samples, double[] depths, DepthEstimator implementation)
    {
        var costs = new double[samples.Count * depths.Length];
        var cpuSeconds = new double[4];
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
        for (var iteration = 0; iteration < cpuSeconds.Length; iteration++)
        {
            var start = Stopwatch.GetTimestamp();
            Parallel.For(0, samples.Count, options, pixel =>
            {
                var sample = samples[pixel];
                for (var plane = 0; plane < depths.Length; plane += 2)
                {
                    var next = Math.Min(plane + 1, depths.Length - 1);
                    var pair = StereoCostBatch.Calculate(views, centre, sample.Ray, depths[plane], depths[next], sample.Patch, sample.Norm);
                    costs[pixel * depths.Length + plane] = pair.First;
                    if (next != plane)
                    {
                        costs[pixel * depths.Length + next] = pair.Second;
                    }
                }
            });
            cpuSeconds[iteration] = Stopwatch.GetElapsedTime(start).TotalSeconds;
        }

        // Validate the decision comparison against the actual production search.
        Parallel.For(0, samples.Count, options, pixel =>
        {
            var sample = samples[pixel];
            var scratch = new double[depths.Length];
            var found = implementation.SearchDepth(views, centre, sample.Ray, sample.Patch, sample.Norm, depths, scratch, true, default, out var depth, out var best);
            var decision = Decide(implementation, views, centre, sample.Ray, depths, costs.AsSpan(pixel * depths.Length, depths.Length));
            var differs = decision.Accepted != found
                || found && decision.Depth != (float)depth
                || found && decision.Confidence != (float)(1 - best);
            if (differs)
            {
                throw new InvalidOperationException("Prototype validator disagrees with the production search.");
            }
        });

        var viewData = new List<float>();
        var grayData = new List<float>();
        foreach (var view in views)
        {
            viewData.AddRange(view.Centre.Array.Select(value => (float)value));
            viewData.AddRange(view.WorldToCamera.Select(value => (float)value));
            viewData.AddRange(view.PatchRight.Array.Select(value => (float)value));
            viewData.AddRange(view.PatchDown.Array.Select(value => (float)value));
            viewData.AddRange([(float)view.Focal, view.Width, view.Height, BitConverter.Int32BitsToSingle(grayData.Count), 0, 0]);
            grayData.AddRange(view.Gray.AsSpan(0, view.Width * view.Height).ToArray());
        }
        var jobs = samples.SelectMany(sample => centre.Array.Concat(sample.Ray.Array).Append(sample.Norm).Concat(sample.Patch))
            .Select(value => (float)value).ToArray();
        WriteArray(Path.Combine(directory, "views.bin"), viewData.ToArray());
        WriteArray(Path.Combine(directory, "gray.bin"), grayData.ToArray());
        WriteArray(Path.Combine(directory, "jobs.bin"), jobs);
        WriteArray(Path.Combine(directory, "depths.bin"), depths.Select(value => (float)value).ToArray());
        WriteArray(Path.Combine(directory, "cpu-costs.bin"), costs);
        var info = new MetalDepthFixtureInfo(width, height, samples.Count, depths.Length, views.Length, imageId, cpuSeconds);
        File.WriteAllText(Path.Combine(directory, "fixture.json"), json.Encode(info));
        Console.WriteLine(json.Encode(info));
    }

    private void Verify(string directory, V3 centre, StereoView[] views, List<MetalDepthSample> samples, double[] depths, DepthEstimator implementation)
    {
        var reference = MemoryMarshal.Cast<byte, double>(File.ReadAllBytes(Path.Combine(directory, "cpu-costs.bin"))).ToArray();
        var actual = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(Path.Combine(directory, "gpu-costs.bin"))).ToArray();
        if (actual.Length != reference.Length || actual.Length != samples.Count * depths.Length)
        {
            throw new InvalidOperationException("GPU cost count does not match the exported fixture.");
        }
        var finiteCosts = 0;
        var finiteSupportChanges = 0;
        var roundedCostChanges = 0;
        var maxCostError = 0.0;
        var acceptedCpu = 0;
        var acceptedGpu = 0;
        var acceptanceChanges = 0;
        var bestPlaneChanges = 0;
        var depthChanges = 0;
        var confidenceChanges = 0;
        var maxRelativeDepthError = 0.0;
        var gpu = actual.Select(value => (double)value).ToArray();
        for (var index = 0; index < reference.Length; index++)
        {
            if (double.IsFinite(reference[index]) != double.IsFinite(gpu[index]))
            {
                finiteSupportChanges++;
            }
            else if (double.IsFinite(reference[index]))
            {
                finiteCosts++;
                roundedCostChanges += (float)reference[index] == actual[index] ? 0 : 1;
                maxCostError = Math.Max(maxCostError, Math.Abs(reference[index] - gpu[index]));
            }
        }
        for (var pixel = 0; pixel < samples.Count; pixel++)
        {
            var cpuDecision = Decide(implementation, views, centre, samples[pixel].Ray, depths, reference.AsSpan(pixel * depths.Length, depths.Length));
            var gpuDecision = Decide(implementation, views, centre, samples[pixel].Ray, depths, gpu.AsSpan(pixel * depths.Length, depths.Length));
            acceptedCpu += cpuDecision.Accepted ? 1 : 0;
            acceptedGpu += gpuDecision.Accepted ? 1 : 0;
            acceptanceChanges += cpuDecision.Accepted != gpuDecision.Accepted ? 1 : 0;
            bestPlaneChanges += cpuDecision.BestPlane != gpuDecision.BestPlane ? 1 : 0;
            if (cpuDecision.Accepted && gpuDecision.Accepted)
            {
                depthChanges += cpuDecision.Depth != gpuDecision.Depth ? 1 : 0;
                confidenceChanges += cpuDecision.Confidence != gpuDecision.Confidence ? 1 : 0;
                maxRelativeDepthError = Math.Max(maxRelativeDepthError, Math.Abs(cpuDecision.Depth - gpuDecision.Depth) / cpuDecision.Depth);
            }
        }
        var report = json.Encode(new
        {
            samples = samples.Count,
            costs = reference.Length,
            finiteCosts,
            finiteSupportChanges,
            roundedCostChanges,
            maxCostError,
            acceptedCpu,
            acceptedGpu,
            acceptanceChanges,
            bestPlaneChanges,
            depthChanges,
            confidenceChanges,
            maxRelativeDepthError
        });
        File.WriteAllText(Path.Combine(directory, "validation.json"), report);
        Console.WriteLine(report);
    }

    private MetalDepthDecision Decide(DepthEstimator implementation, StereoView[] views, V3 centre, V3 ray,
        double[] depths, ReadOnlySpan<double> costs)
    {
        var best = double.PositiveInfinity;
        var bestPlane = -1;
        for (var plane = 0; plane < costs.Length; plane++)
        {
            if (costs[plane] < best)
            {
                best = costs[plane];
                bestPlane = plane;
            }
        }
        if (bestPlane < 0 || best >= .18)
        {
            return new MetalDepthDecision(false, bestPlane, 0, 0);
        }
        for (var plane = 0; plane < costs.Length; plane++)
        {
            var competing = plane != bestPlane && costs[plane] - best < .015;
            if (competing && implementation.DistinctProjection(views, centre, ray, depths[bestPlane], depths[plane]))
            {
                return new MetalDepthDecision(false, bestPlane, 0, 0);
            }
        }
        var depth = implementation.InterpolateDepth(depths, costs, bestPlane, best);
        return new MetalDepthDecision(true, bestPlane, (float)depth, (float)(1 - best));
    }

    private void WriteArray<T>(string path, T[] values) where T : unmanaged
    {
        File.WriteAllBytes(path, MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
    }
}
