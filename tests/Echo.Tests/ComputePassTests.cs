using System.ComponentModel.Composition;
using Echo.Compute;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Depth;
using Echo.Features;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Meshing;
using Echo.Matching;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class ComputePassTests
{
    private readonly IComputeService compute;
    private readonly IDepthEstimator depthEstimator;

    [ImportingConstructor]
    public ComputePassTests(IComputeService compute, IDepthEstimator depthEstimator)
    {
        this.compute = compute;
        this.depthEstimator = depthEstimator;
    }

    internal void Run(TestSuite tests)
    {
        using (var probe = compute.Open(ComputeBackend.Auto, default))
        {
            if (probe == null)
            {
                Console.WriteLine("SKIP compute passes: Metal is unavailable");
                return;
            }
        }
        tests.Run("Metal Gaussian matches the scalar filter including borders and aliased planes", Gaussian);
        tests.Run("Concurrent Metal feature pyramids retain deterministic descriptors", ConcurrentFeatures);
        tests.Run("Metal depth filtering preserves missing, isolated and boundary samples", DepthFilter);
        tests.Run("Metal cloud support preserves discontinuity and reprojection decisions", CloudSupport);
        tests.Run("Metal fusion preserves the observed field and sample ordering", Fusion);
        tests.Run("Metal sparse extraction preserves topology and full integer grid coordinates", Extraction);
        tests.Run("Metal visibility and semantic projection retain CPU evidence decisions", Visibility);
        tests.Run("Metal guided matching rejects off-line descriptor aliases", GuidedMatching);
        tests.Run("Metal batched epipolar verification preserves inliers and adaptive stopping", Epipolar);
        tests.Run("Metal triangulation preserves large origins, low parallax and rejected matches", Triangulation);
        tests.Run("Metal UV warm starts retain double precision residual convergence", UvSolve);
        tests.Run("Metal seam diffusion preserves anchors and propagation", Seams);
        tests.Run("Metal padding preserves original colour, distance and tie ordering", Padding);
    }

    private void Gaussian()
    {
        var source = Enumerable.Range(0, 129 * 128).Select(i => (float)Math.Sin(i * .31)).ToArray();
        var cpu = new float[source.Length];
        using var session = compute.Open(ComputeBackend.Metal, default)!;
        foreach (var sigma in new[] { .3, 1.6, 5.1 })
        {
            GaussianBlur.Apply(source, cpu, 129, 128, sigma, default);
            var gpu = source.ToArray();
            GaussianBlur.Apply(gpu, gpu, 129, 128, sigma, default, session);
            Check(cpu.Zip(gpu).All(pair => Math.Abs(pair.First - pair.Second) < 2e-6), "Gaussian error exceeds 2e-6");
        }
    }

    private void ConcurrentFeatures()
    {
        const int width = 256;
        var random = new Random(912);
        var source = Enumerable.Range(0, width * width).Select(_ => (float)random.NextDouble()).ToArray();
        var expected = ScaleSpaceFeatures.Detect(source, width, width, 128, default, compute, ComputeBackend.Metal);
        Check(expected.Count > 0, "Feature fixture must produce descriptors");
        Parallel.For(0, 3, _ =>
        {
            var actual = ScaleSpaceFeatures.Detect(source, width, width, 128, default, compute, ComputeBackend.Metal);
            Check(actual.Count == expected.Count, "Concurrent pyramid changed the candidate count");
            for (var i = 0; i < actual.Count; i++)
            {
                Check(actual[i].X == expected[i].X && actual[i].Y == expected[i].Y && actual[i].Descriptor.SequenceEqual(expected[i].Descriptor),
                    "Releasing the device between blurs changed a descriptor");
            }
        });
    }

    private void DepthFilter()
    {
        var raw = Enumerable.Range(0, 128 * 128).Select(i => i % 97 == 0 ? 0f : 10f + i % 7 * .01f).ToArray();
        raw[300] = 25;
        raw[600] = float.NaN;
        raw[900] = float.PositiveInfinity;
        var cpu = new DepthMap { Width = 128, Height = 128, Depth = raw.ToArray(), Confidence = Enumerable.Repeat(.8f, raw.Length).ToArray() };
        var gpu = new DepthMap { Width = 128, Height = 128, Depth = raw.ToArray(), Confidence = cpu.Confidence.ToArray() };
        var expected = ((DepthEstimator)depthEstimator).FilterUnsupportedSamples(cpu, default);
        var actual = DepthFilterCompute.TryFilter(compute, ComputeBackend.Metal, gpu, default);
        Check(actual == expected, "Retained depth count differs");
        Check(cpu.Depth.SequenceEqual(gpu.Depth) && cpu.Confidence.SequenceEqual(gpu.Confidence), "Depth or confidence differs");
    }

    private void CloudSupport()
    {
        var fixture = new FusionFixture();
        fixture.Project.Settings.ComputeBackend = ComputeBackend.Metal;
        var reference = fixture.Views[0];
        var neighbour = fixture.Views[1];
        for (var i = 0; i < neighbour.Map.Depth.Length; i += 47)
        {
            neighbour.Map.Depth[i] = i % 2 == 0 ? 0 : 30;
        }
        var settings = fixture.Project.Settings;
        var gpu = CloudSupportCompute.TryCalculate(compute, reference, [neighbour], settings, default)!;
        Check(gpu != null, "GPU cloud path was skipped");
        var minimumSine = Math.Sin(settings.CloudMinimumParallaxDegrees * Math.PI / 180);
        for (var y = 0; y < reference.Map.Height; y++)
        {
            for (var x = 0; x < reference.Map.Width; x++)
            {
                var point = reference.Point(x, y, reference.Map.DepthAt(y * reference.Map.Width + x));
                var a = point - reference.Centre;
                var b = point - neighbour.Centre;
                var cross = a.Cross(b);
                var parallax = cross.Dot(cross) >= minimumSine * minimumSine * a.Dot(a) * b.Dot(b);
                var supported = parallax && neighbour.TrySurfacePoint(point, settings.CloudDepthTolerance, out var surface)
                    && reference.ReprojectsTo(surface, x, y, settings.CloudReprojectionTolerancePixels);
                Check(gpu![y * reference.Map.Width + x] == (supported ? 1 : 0), "Cloud support differs at " + x + "," + y);
            }
        }
    }

    private void Fusion()
    {
        var fixture = new FusionFixture();
        var fusion = new DepthFusion(compute);
        var resources = new FusionResourcePlan(64 * 1024 * 1024, 100000);
        fixture.Project.Settings.ComputeBackend = ComputeBackend.Cpu;
        var cpu = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default);
        fixture.Project.Settings.ComputeBackend = ComputeBackend.Metal;
        var gpu = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default);
        Check(cpu.Samples.Count == gpu.Samples.Count && cpu.Samples.Count > 0, "Fusion coverage differs");
        for (var i = 0; i < cpu.Samples.Count; i++)
        {
            var a = cpu.Samples[i];
            var b = gpu.Samples[i];
            Check(a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.Views == b.Views, "Fusion ordering or support differs");
            Check(Math.Abs(a.Distance - b.Distance) < 2e-5, "Fusion field differs");
        }
    }

    private void Extraction()
    {
        var samples = new Dictionary<Echo.Meshing.Grid, float>();
        for (var x = 0; x < 18; x++)
        {
            for (var y = 0; y < 18; y++)
            {
                for (var z = 0; z < 18; z++)
                {
                    samples.Add(new Echo.Meshing.Grid(20000001 + x, -20000001 + y, z), z - 8.3f);
                }
            }
        }
        samples.Remove(new Echo.Meshing.Grid(20000009, -19999993, 9));
        var cpu = TetrahedralSurface.Extract(samples, .1, default);
        var gpu = TetrahedralSurface.Extract(samples, .1, default, compute: compute, backend: ComputeBackend.Metal);
        Check(cpu.Vertices.Count > 0 && cpu.Vertices.Count == gpu.Vertices.Count && cpu.Faces.Count == gpu.Faces.Count, "Extracted topology differs");
        Check(cpu.Vertices.Zip(gpu.Vertices).All(pair => pair.First.SequenceEqual(pair.Second)), "Vertex coordinates differ");
        Check(cpu.Faces.Zip(gpu.Faces).All(pair => pair.First.SequenceEqual(pair.Second)), "Face indices differ");
    }

    private void Visibility()
    {
        var project = new ProjectData();
        var scene = new ReconstructionScene();
        var maps = new List<DepthMap>();
        var semantics = new Dictionary<string, SemanticMap>();
        for (var i = 0; i < 3; i++)
        {
            var id = i.ToString();
            project.Images.Add(new Photo { Id = id, Width = 64, Height = 64, FocalPixels = 64 });
            scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [i - 1, 0, 0] });
            maps.Add(new DepthMap { ImageId = id, Width = 64, Height = 64, Depth = Enumerable.Repeat(10f, 4096).ToArray() });
            semantics.Add(id, new SemanticMap { ImageId = id, Width = 8, Height = 8, Vegetation = Enumerable.Repeat((byte)255, 64).ToArray(), Rock = new byte[64] });
        }
        var surface = new Surface { Vertices = [[-.5, -.5, 10], [.5, -.5, 10], [0, .5, 10], [-1, -1, 8], [1, -1, 8], [0, 1, 8]], Faces = [[0, 1, 2], [3, 4, 5]] };
        project.Settings.ComputeBackend = ComputeBackend.Cpu;
        var cpu = SurfaceVisibilityFilter.Filter(surface, project, scene, maps, default);
        project.Settings.ComputeBackend = ComputeBackend.Metal;
        var gpu = SurfaceVisibilityFilter.Filter(surface, project, scene, maps, default, compute: compute);
        Check(cpu.Faces.Count == 1 && gpu.Faces.Count == 1, "Free space rejection differs");
        for (var variation = 0; variation < 3; variation++)
        {
            project.Settings.ComputeBackend = ComputeBackend.Cpu;
            var expected = SurfaceVegetationClassifier.Classify(cpu, project, scene, maps, semantics, default);
            project.Settings.ComputeBackend = ComputeBackend.Metal;
            var actual = SurfaceVegetationClassifier.Classify(cpu, project, scene, maps, semantics, default, compute: compute);
            Check(expected.SequenceEqual(actual), "Vegetation votes differ");
            if (variation == 0)
            {
                Array.Fill(semantics["2"].Rock, (byte)255);
            }
            else
            {
                Array.Fill(maps[2].Depth, 2f);
            }
        }
    }

    private void GuidedMatching()
    {
        var random = new Random(521);
        var photo = new Photo { Width = 1024, Height = 768, FocalPixels = 900 };
        var camera = new CameraPose { Centre = [1, .1, 0] };
        var left = new FeatureSet { ImageId = "left" };
        var right = new FeatureSet { ImageId = "right" };
        var seed = new List<Match>();
        for (var i = 0; i < 300; i++)
        {
            var world = new V3(random.NextDouble() * 4 - 2, random.NextDouble() * 3 - 1, random.NextDouble() * 20 + 8);
            var a = CameraGeometry.ProjectPoint(photo, new CameraPose(), world)!.Value;
            var b = CameraGeometry.ProjectPoint(photo, camera, world)!.Value;
            var descriptor = Enumerable.Range(0, 32).Select(_ => (float)random.NextDouble() * .1f).ToArray();
            left.Features.Add(new Feature { X = a.X, Y = a.Y, Descriptor = descriptor });
            right.Features.Add(new Feature { X = b.X, Y = b.Y, Descriptor = descriptor });
            right.Features.Add(new Feature { X = b.X + 30, Y = b.Y + 180, Descriptor = descriptor });
            if (i < 30)
            {
                seed.Add(new Match { A = [a.X, a.Y], B = [b.X, b.Y] });
            }
        }
        var matcher = new GuidedFeatureMatcher(compute);
        var cpu = matcher.Recover(left, right, seed, new Settings { ComputeBackend = ComputeBackend.Cpu }, 1, default)!;
        var gpu = matcher.Recover(left, right, seed, new Settings { ComputeBackend = ComputeBackend.Metal }, 1, default)!;
        Check(cpu != null && gpu != null && cpu.Matches.Count == 300 && gpu.Matches.Count == cpu.Matches.Count, "Guided coverage differs");
        Check(cpu!.Matches.Zip(gpu!.Matches).All(pair => pair.First.A.SequenceEqual(pair.Second.A) && pair.First.B.SequenceEqual(pair.Second.B)), "Guided matches or ordering differ");
    }

    private void Epipolar()
    {
        var random = new Random(91);
        var photo = new Photo { Width = 1024, Height = 768, FocalPixels = 900 };
        var a = new CameraPose();
        var b = new CameraPose { Centre = [1, .1, .05] };
        var matches = new List<Match>();
        for (var i = 0; i < 1200; i++)
        {
            var point = new V3(random.NextDouble() * 5 - 2.5, random.NextDouble() * 4 - 2, 8 + random.NextDouble() * 20);
            var left = CameraGeometry.ProjectPoint(photo, a, point)!.Value;
            var right = CameraGeometry.ProjectPoint(photo, b, point)!.Value;
            matches.Add(new Match { A = [left.X, left.Y], B = [right.X, i % 5 == 0 ? right.Y + 100 : right.Y] });
        }
        var cpu = FundamentalMatrixEstimator.Estimate(matches, 2, default)!;
        var gpu = FundamentalMatrixEstimator.Estimate(matches, 2, default, compute, ComputeBackend.Metal)!;
        Check(cpu.Inliers.SequenceEqual(gpu.Inliers) && cpu.Iterations == gpu.Iterations, "RANSAC result or adaptive stop differs");
    }

    private void Triangulation()
    {
        var photo = new Photo { Width = 1024, Height = 768, FocalPixels = 900 };
        var origin = new V3(6000000, 2000000, -1000000);
        var a = new CameraPose { Centre = origin.Array };
        var b = new CameraPose { Centre = (origin + new V3(1, 0, 0)).Array };
        var matches = new List<Match>();
        for (var i = 0; i < 600; i++)
        {
            var point = origin + new V3(i % 20 * .1 - 1, i % 13 * .1 - .6, i % 7 == 0 ? 250 : 10 + i % 9);
            var left = CameraGeometry.ProjectPoint(photo, a, point)!.Value;
            var right = CameraGeometry.ProjectPoint(photo, b, point)!.Value;
            matches.Add(new Match { A = [left.X, left.Y], B = [right.X, right.Y + (i % 11 == 0 ? 40 : 0)] });
        }
        var gpu = TriangulationCompute.TryCalculate(compute, ComputeBackend.Metal, photo, photo, a, b, matches, 2, default)!;
        Check(gpu != null, "Triangulation was skipped");
        for (var i = 0; i < matches.Count; i++)
        {
            var cpu = CameraGeometry.Triangulate(photo, photo, a, b, matches[i], 2);
            Check((cpu == null) == (gpu![i] == null), "Triangulation acceptance differs");
            if (cpu != null)
            {
                Check((cpu.Point - gpu[i]!.Point).Length < .003, "Triangulation position differs by more than 3 mm");
            }
        }
    }

    private void UvSolve()
    {
        const int count = 2048;
        var system = new TextureLinearSystem(count);
        for (var i = 0; i < count; i++)
        {
            system.Add(i, i, 2);
            if (i > 0)
            {
                system.Add(i, i - 1, -1);
            }
            if (i + 1 < count)
            {
                system.Add(i, i + 1, -1);
            }
        }
        var constraints = new Dictionary<int, double> { [0] = 0, [count - 1] = 1 };
        var gpu = system.Solve(constraints, default, compute, ComputeBackend.Metal);
        Check(gpu != null, "UV solve failed to converge");
        for (var i = 0; i < count; i++)
        {
            Check(Math.Abs(gpu![i] - (double)i / (count - 1)) < 1e-7, "UV solve deviates from the analytic harmonic map");
        }
    }

    private void Seams()
    {
        var nodes = Enumerable.Range(0, 1024).Select(i => new TextureSeamNode { Weight = i % 32 == 0 ? 1 : 0 }).ToList();
        var local = new V3[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].Neighbours.Add((i + 1) % nodes.Count);
            nodes[i].Neighbours.Add((i + nodes.Count - 1) % nodes.Count);
            local[i] = nodes[i].Weight > 0 ? new V3(.3, -.2, .1) : default;
        }
        var expected = local.ToArray();
        for (var pass = 0; pass < 32; pass++)
        {
            var next = new V3[expected.Length];
            for (var i = 0; i < expected.Length; i++)
            {
                next[i] = nodes[i].Weight > 0 ? local[i] : (expected[nodes[i].Neighbours[0]] + expected[nodes[i].Neighbours[1]]) / 2.05;
            }
            expected = next;
        }
        var actual = SeamDiffusionCompute.TryDiffuse(compute, ComputeBackend.Metal, nodes, local, default)!;
        Check(actual != null && expected.Zip(actual).All(pair => (pair.First - pair.Second).Length < 1e-6), "Seam propagation differs");
    }

    private void Padding()
    {
        const int size = 256;
        var cpu = new SKColor[size * size];
        for (var y = 0; y < size; y += 13)
        {
            for (var x = 0; x < size; x += 11)
            {
                cpu[y * size + x] = new SKColor((byte)x, (byte)y, 99, 255);
            }
        }
        var gpu = cpu.ToArray();
        TexturePadding.Extend(cpu, size, 8, default);
        Check(TexturePaddingCompute.TryExtend(compute, ComputeBackend.Metal, gpu, size, 8, default), "Padding was skipped");
        Check(cpu.SequenceEqual(gpu), "Padding differs including equidistant source ties");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
