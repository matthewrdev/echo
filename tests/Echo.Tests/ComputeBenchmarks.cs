using System.Diagnostics;
using Echo.Compute;
using Echo.Depth;
using Echo.Features;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Matching;
using Echo.Meshing.Geometry;
using Echo.Meshing.Visibility;
using Echo.Meshing.Volumes;
using Echo.Meshing;
using Echo.Models;
using Echo.Texturing.Charts;
using Echo.Texturing.Seams;
using Echo.Texturing;

namespace Echo.Tests;

// Opt-in microbenchmarks include staging and readback, after kernel warmup.
public sealed class ComputeBenchmarks
{
    private readonly IComputeService compute;
    private readonly IDepthEstimator depthEstimator;

    public ComputeBenchmarks(IComputeService compute, IDepthEstimator depthEstimator)
    {
        this.compute = compute;
        this.depthEstimator = depthEstimator;
    }

    internal void Run()
    {
        var random = new Random(814);
        var left = Enumerable.Range(0, 1024).Select(_ => new Feature
        {
            Descriptor = Enumerable.Range(0, 128).Select(_ => (float)random.NextDouble() * .1f).ToArray()
        }).ToArray();
        var right = left.Select(feature => new Feature { Descriptor = feature.Descriptor.Select(value => value + .0001f).ToArray() }).ToArray();
        var cpuSettings = new Settings { ComputeBackend = ComputeBackend.Cpu };
        var gpuSettings = new Settings { ComputeBackend = ComputeBackend.Metal };
        Measure("Descriptors 1024 x 1024 x 128",
            () => DescriptorMatcher.Match<Feature>(left, right, cpuSettings, default, false, compute),
            () => DescriptorMatcher.Match<Feature>(left, right, gpuSettings, default, false, compute));

        var source = Enumerable.Range(0, 2048 * 2048).Select(i => (float)Math.Sin(i)).ToArray();
        var destination = new float[source.Length];
        Measure("Gaussian 2048 x 2048 sigma 1.6",
            () => GaussianBlur.Apply(source, destination, 2048, 2048, 1.6, default),
            () =>
            {
                using var session = compute.Open(ComputeBackend.Metal, default)!;
                GaussianBlur.Apply(source, destination, 2048, 2048, 1.6, default, session);
            });
        var raw = Enumerable.Range(0, 1024 * 1024).Select(i => i % 97 == 0 ? 0f : 10f + i % 7 * .01f).ToArray();
        var map = new DepthMap { Width = 1024, Height = 1024, Depth = new float[raw.Length], Confidence = new float[raw.Length] };
        Measure("Depth cleanup 1024 x 1024",
            () =>
            {
                raw.CopyTo(map.Depth, 0);
                ((DepthEstimator)depthEstimator).FilterUnsupportedSamples(map, default);
            },
            () =>
            {
                raw.CopyTo(map.Depth, 0);
                DepthFilterCompute.TryFilter(compute, ComputeBackend.Metal, map, default);
            });
        FusionAndExtraction();
        SurfaceOcclusion();
        Geometry();
        UvAndSeams();
    }

    private void FusionAndExtraction()
    {
        var fixture = new FusionFixture();
        var fusion = new DepthFusion(compute);
        var resources = new FusionResourcePlan(64 * 1024 * 1024, 100000);
        Measure("Fusion observed-plane fixture",
            () =>
            {
                fixture.Project.Settings.ComputeBackend = ComputeBackend.Cpu;
                fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default);
            },
            () =>
            {
                fixture.Project.Settings.ComputeBackend = ComputeBackend.Metal;
                fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default);
            });
        var samples = new Dictionary<Echo.Meshing.Volumes.Grid, float>();
        for (var x = 0; x < 50; x++)
        {
            for (var y = 0; y < 50; y++)
            {
                for (var z = 0; z < 50; z++)
                {
                    samples.Add(new Echo.Meshing.Volumes.Grid(x, y, z), z - 24.3f);
                }
            }
        }

        Measure("Surface extraction 125000 lattice samples",
            () => TetrahedralSurface.Extract(samples, .1, default),
            () => TetrahedralSurface.Extract(samples, .1, default, compute: compute, backend: ComputeBackend.Metal));
    }

    private void SurfaceOcclusion()
    {
        const int width = 128;
        var mesh = new Surface();
        for (var y = 0; y <= width; y++)
        {
            for (var x = 0; x <= width; x++)
            {
                mesh.Vertices.Add([x, y, Math.Sin(x * .1) * Math.Cos(y * .1)]);
            }
        }

        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var first = y * (width + 1) + x;
                mesh.Faces.Add([first, first + 1, first + width + 1]);
                mesh.Faces.Add([first + 1, first + width + 2, first + width + 1]);
            }
        }

        using var index = new SurfaceTriangleIndex(mesh, default);
        var random = new Random(712);
        var rays = new SurfaceRay[65536];
        for (var ray = 0; ray < rays.Length; ray++)
        {
            var triangle = index.Triangle(ray % mesh.Faces.Count);
            rays[ray] = new SurfaceRay(new V3(random.NextDouble() * width, random.NextDouble() * width, 30),
                (triangle.A + triangle.B + triangle.C) / 3);
        }

        var cpu = new bool[rays.Length];
        var gpu = new bool[rays.Length];
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
        Measure("Surface occlusion 32768 faces, 65536 rays including BVH upload",
            () => Parallel.For(0, rays.Length, parallel, ray => cpu[ray] = index.HasOccluder(rays[ray].Start, rays[ray].End)),
            () =>
            {
                using var acceleration = SurfaceOcclusionPreparation.Open(compute, ComputeBackend.Metal, index, default)!;
                acceleration.Evaluate(rays, gpu, default);
            });
        if (!cpu.SequenceEqual(gpu))
        {
            throw new InvalidOperationException("Occlusion benchmark changed visibility decisions.");
        }
    }

    private void Geometry()
    {
        var random = new Random(91);
        var photo = new Photo { Width = 1024, Height = 768, FocalPixels = 900 };
        var a = new CameraPose();
        var b = new CameraPose { Centre = [1, .1, .05] };
        var matches = new List<Match>();
        for (var i = 0; i < 30000; i++)
        {
            var point = new V3(random.NextDouble() * 5 - 2.5, random.NextDouble() * 4 - 2, 8 + random.NextDouble() * 20);
            var left = CameraGeometry.ProjectPoint(photo, a, point)!.Value;
            var right = CameraGeometry.ProjectPoint(photo, b, point)!.Value;
            matches.Add(new Match { A = [left.X, left.Y], B = [right.X, i % 5 == 0 ? right.Y + 100 : right.Y] });
        }

        Measure("Epipolar verification 30000 matches",
            () => FundamentalMatrixEstimator.Estimate(matches, 2, default),
            () => FundamentalMatrixEstimator.Estimate(matches, 2, default, compute, ComputeBackend.Metal));
        Measure("Triangulation 30000 matches",
            () =>
            {
                foreach (var match in matches)
                {
                    CameraGeometry.Triangulate(photo, photo, a, b, match, 2);
                }
            },
            () => TriangulationCompute.TryCalculate(compute, ComputeBackend.Metal, photo, photo, a, b, matches, 2, default));
    }

    private void UvAndSeams()
    {
        const int width = 128;
        var system = new TextureLinearSystem(width * width);
        var constraints = new Dictionary<int, double>();
        var nodes = new List<TextureSeamNode>();
        var local = new V3[width * width];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var boundary = x == 0 || y == 0 || x == width - 1 || y == width - 1;
                if (boundary)
                {
                    constraints[index] = (double)y / (width - 1);
                }

                var node = new TextureSeamNode { Weight = boundary ? 1 : 0 };
                nodes.Add(node);
                local[index] = boundary ? new V3(.3, -.2, .1) : default;
                foreach (var adjacent in new[] { index - 1, index + 1, index - width, index + width })
                {
                    if (adjacent >= 0 && adjacent < width * width)
                    {
                        node.Neighbours.Add(adjacent);
                        system.Add(index, adjacent, -1);
                    }
                }

                system.Add(index, index, node.Neighbours.Count);
            }
        }

        Measure("UV solve 16384 unknowns with double refinement",
            () => system.Solve(constraints, default),
            () => system.Solve(constraints, default, compute, ComputeBackend.Metal));
        Measure("Seam diffusion 16384 nodes, 32 passes",
            () =>
            {
                var current = local.ToArray();
                var next = new V3[local.Length];
                for (var pass = 0; pass < 32; pass++)
                {
                    for (var i = 0; i < current.Length; i++)
                    {
                        var sum = new V3();
                        foreach (var adjacent in nodes[i].Neighbours)
                        {
                            sum += current[adjacent];
                        }

                        next[i] = nodes[i].Weight > 0 ? local[i] : sum / (nodes[i].Neighbours.Count + .05);
                    }

                    var previous = current;
                    current = next;
                    next = previous;
                }
            },
            () => SeamDiffusionCompute.TryDiffuse(compute, ComputeBackend.Metal, nodes, local, default));
    }

    private void Measure(string name, Action cpu, Action gpu)
    {
        cpu();
        gpu();
        var cpuTimes = new double[3];
        var gpuTimes = new double[3];
        for (var i = 0; i < cpuTimes.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            cpu();
            cpuTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            gpu();
            gpuTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(cpuTimes);
        Array.Sort(gpuTimes);
        Console.WriteLine($"BENCH {name}: CPU {cpuTimes[1]:F2} ms; Metal {gpuTimes[1]:F2} ms; {cpuTimes[1] / gpuTimes[1]:F2}x");
    }
}
