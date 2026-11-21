using Echo.Compute;
using Echo.Contracts;
using System.Diagnostics;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

public sealed class TexturePackingTests
{
    private readonly ITexturePatchPacker packer;
    private readonly IComputeService compute;
    private readonly ITextureProfilePacker profiles;
    private readonly ITexturePlacementAccelerator[] accelerators;

    public TexturePackingTests(ITexturePatchPacker packer, ITextureProfilePacker profiles,
        IEnumerable<ITexturePlacementAccelerator> accelerators, IComputeService compute)
    {
        this.packer = packer;
        this.compute = compute;
        this.profiles = profiles;
        this.accelerators = accelerators.ToArray();
    }

    internal void Run(TestSuite tests, bool requireMetal = false)
    {
        tests.Run("Small islands fill a large island's cavity without moving or cutting it", () =>
        {
            var patches = CavityFixture();
            Check(!profiles.TryPack(patches, 128, 2, 1, default), "The skyline cannot recover this cavity");
            var gaps = new TextureGapPacker([]);
            var accelerated = false;
            Check(gaps.TryPack(patches[..1], 128, 2, 1, 2, null, ref accelerated, default), "Large chart fits alone");
            var anchor = patches[0].AtlasBounds;
            var rotation = patches[0].QuarterTurns;
            Check(gaps.TryPack(patches, 128, 2, 1, 2, null, ref accelerated, default), "All small charts fit in the existing gaps");
            Check(patches[0].AtlasBounds == anchor && patches[0].QuarterTurns == rotation, "Large chart placement stays fixed");
            Check(patches[0].Triangles.Count == 6, "Large chart stays intact");
            Check(patches.Skip(1).Any(patch => anchor.Contains(patch.AtlasBounds)), "Small charts actually occupy the large chart's bounding box");
            ValidatePixels(patches, 128, 2, 1);
        });
        tests.Run("Gap packing increases texel density and preserves deterministic padded UVs", () =>
        {
            var patches = CavityFixture();
            var baseline = ((TexturePatchPacker)packer).TryPackSkyline(patches, 128, 2, default)!.Value;
            var timer = Stopwatch.StartNew();
            var messages = new List<string>();
            var scale = packer.Pack(patches, 128, 2, progress: messages.Add);
            Check(scale > baseline * 1.1, "Recovered cavities must buy more than ten percent linear texture resolution");
            Console.WriteLine($"  Cavity fixture: {baseline:F4} -> {scale:F4} texels/unit (+{100 * (scale / baseline - 1):F1}%), {timer.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine("  " + messages.First(message => message.StartsWith("Filling gaps", StringComparison.Ordinal)));
            ValidatePixels(patches, 128, 2, scale);
            var repeated = CavityFixture();
            var repeatedScale = packer.Pack(repeated, 128, 2, backend: ComputeBackend.Cpu);
            Check(scale == repeatedScale, "CPU and automatic scale searches are deterministic");
            for (var index = 0; index < patches.Length; index++)
            {
                Check(patches[index].AtlasBounds == repeated[index].AtlasBounds, "Repeated positions match");
                Check(patches[index].QuarterTurns == repeated[index].QuarterTurns, "Repeated rotations match");
            }
        });
        tests.Run("Packing honours CPU, automatic fallback and explicitly requested Metal", () =>
        {
            var unavailable = new UnavailableTexturePlacementAccelerator();
            var gaps = new TextureGapPacker([unavailable]);
            var patches = CavityFixture();
            var baseline = ((TexturePatchPacker)packer).TryPackSkyline(patches, 128, 2, default)!.Value;
            var cpu = gaps.Improve(patches, 128, 2, baseline, default, backend: ComputeBackend.Cpu);
            Check(unavailable.OpenCount == 0, "CPU selection never opens a Metal device");
            patches = CavityFixture();
            ((TexturePatchPacker)packer).TryPackSkyline(patches, 128, 2, default);
            var automatic = gaps.Improve(patches, 128, 2, baseline, default);
            Check(unavailable.OpenCount == 1 && automatic == cpu, "Automatic fallback preserves the CPU layout density");
            try
            {
                gaps.Improve(patches, 128, 2, baseline, default, backend: ComputeBackend.Metal);
                throw new Exception("Explicit Metal silently fell back");
            }
            catch (TextureAccelerationException)
            {
                Check(unavailable.OpenCount == 2, "Explicit Metal requested the device");
            }
        });
        tests.Run("Unsuccessful gap searches retain the complete skyline layout and scale", () =>
        {
            var patches = new[] { Rectangle(50, 50), Rectangle(50, 50) };
            var baseline = ((TexturePatchPacker)packer).TryPackSkyline(patches, 128, 2, default)!.Value;
            var saved = patches.Select(patch => new TexturePatchPlacement(patch.AtlasBounds, patch.QuarterTurns)).ToArray();
            var gaps = new TextureGapPacker([]);
            var scale = gaps.Improve(patches, 128, 2, baseline, default);
            Check(scale == baseline, "A denser skyline must never be traded away");
            for (var index = 0; index < patches.Length; index++)
            {
                Check(patches[index].AtlasBounds == saved[index].Bounds && patches[index].QuarterTurns == saved[index].QuarterTurns,
                    "Failed attempts restore every placement");
            }
            ValidatePixels(patches, 128, 2, scale);
        });
        tests.Run("Packing budgets retain complete layouts and agree across CPU and Metal", () =>
        {
            using var probe = OpenMetal(64, requireMetal);
            var gaps = new TextureGapPacker(accelerators, compute);
            foreach (var budget in new long[] { 1, 8192, 65536 })
            {
                var cpu = CavityFixture();
                var baseline = ((TexturePatchPacker)packer).TryPackSkyline(cpu, 128, 2, default)!.Value;
                var saved = cpu.Select(patch => new TexturePatchPlacement(patch.AtlasBounds, patch.QuarterTurns)).ToArray();
                var messages = new List<string>();
                var scale = gaps.Improve(cpu, 128, 2, baseline, default, messages.Add, ComputeBackend.Cpu, budget);
                Check(scale >= baseline, "Budget cannot discard the baseline");
                ValidatePixels(cpu, 128, 2, scale);
                if (budget == 1)
                {
                    Check(messages.Any(message => message.Contains("candidate budget", StringComparison.Ordinal)), "Exhaustion must be reported");
                    Check(scale == baseline && cpu.Select(patch => new TexturePatchPlacement(patch.AtlasBounds, patch.QuarterTurns)).SequenceEqual(saved),
                        "Partial candidates must restore every baseline placement");
                }
                if (probe != null)
                {
                    var gpu = CavityFixture();
                    ((TexturePatchPacker)packer).TryPackSkyline(gpu, 128, 2, default);
                    using var scope = compute.BeginScope(1024 * 1024);
                    var gpuScale = gaps.Improve(gpu, 128, 2, baseline, default, backend: ComputeBackend.Metal, maximumCandidates: budget);
                    Check(gpuScale == scale, "Budget must stop at the same search boundary on both backends");
                    for (var i = 0; i < cpu.Length; i++)
                    {
                        Check(cpu[i].AtlasBounds == gpu[i].AtlasBounds && cpu[i].QuarterTurns == gpu[i].QuarterTurns, "Budget changed backend placement parity");
                    }
                    if (budget > 1)
                    {
                        Check(scope.Executions.Any(execution => execution.Operation == ComputeOperation.TexturePacking
                            && execution.Backend == ComputeBackend.Metal && execution.Precision == ComputePrecision.IntegerExact),
                            "Packing must record its own integer Metal execution");
                    }
                }
            }
        });
        tests.Run("Mask search handles word boundaries, rotations, full atlases and cancellation", () =>
        {
            var atlas = new TextureAtlasOccupancy(65);
            atlas.Place(SolidMask(32, 65), 0, 0);
            var patch = Rectangle(40, 8);
            var masks = Masks(patch, 1, 0);
            var placement = TextureMaskSearch.Find(atlas, masks, 65, default);
            Check(placement != null && placement.QuarterTurns == 1 && placement.X == 32, "Rotate to fit across a bitset word boundary");
            atlas.Place(SolidMask(33, 65), 32, 0);
            Check(TextureMaskSearch.Find(atlas, masks, 65, default) == null, "Full atlas has no placement");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                packer.Pack(CavityFixture(), 128, 2, cancellation.Token);
                throw new Exception("Cancelled packing was accepted");
            }
            catch (OperationCanceledException)
            {
            }
        });
        tests.Run("Metal and CPU choose identical placements for fragmented and full occupancy", () =>
        {
            using var session = OpenMetal(65, requireMetal);
            if (session == null)
            {
                return;
            }
            Console.WriteLine("  Verified backend: " + session.Backend);
            var random = new Random(7921);
            for (var attempt = 0; attempt < 48; attempt++)
            {
                var atlas = new TextureAtlasOccupancy(65);
                for (var index = 0; index < attempt * 2; index++)
                {
                    var width = random.Next(1, 24);
                    var height = random.Next(1, 24);
                    atlas.Place(SolidMask(width, height), random.Next(66 - width), random.Next(66 - height));
                }
                var patch = attempt % 2 == 0 ? CavityFixture()[0] : Rectangle(15.7f, 5.3f);
                var masks = Masks(patch, attempt % 2 == 0 ? .31 : 1, attempt % 3);
                var maximumBottom = attempt % 4 == 0 ? 20 : 65;
                var expected = TextureMaskSearch.Find(atlas, masks, maximumBottom, default);
                var actual = session.Find(atlas, masks, maximumBottom, default);
                Check(actual == expected, $"CPU/Metal placement mismatch for fragmentation case {attempt}: {expected} / {actual}");
            }
            var incremental = new TextureAtlasOccupancy(65);
            var small = Masks(Rectangle(4, 3), 1, 0);
            for (var step = 0; step < 24; step++)
            {
                var minimum = step % 2 == 0 ? 1 : 20;
                var expected = TextureMaskSearch.Find(incremental, small, 65, default, minimum);
                Check(session.Find(incremental, small, 65, default, minimum) == expected, "Dirty-row upload changed placement");
                Check(session.Find(incremental, small, 65, default, minimum) == expected, "Unchanged occupancy must remain resident");
                if (expected != null)
                {
                    incremental.Place(small[expected.QuarterTurns], expected.X, expected.Y);
                    if (step % 4 == 0)
                    {
                        // Missing multiple revisions requires a complete refresh.
                        incremental.Place(SolidMask(1, 1), 64, step);
                    }
                }
            }
            var full = new TextureAtlasOccupancy(65);
            full.Place(SolidMask(65, 65), 0, 0);
            Check(session.Find(full, Masks(Rectangle(1, 1), 1, 0), 65, default) == null, "Metal reports exhausted atlas");
        });
    }

    private ITexturePlacementSession? OpenMetal(int size, bool required)
    {
        try
        {
            if (accelerators.Length == 0)
            {
                throw new TextureAccelerationException("No accelerator composed");
            }
            return accelerators[0].Open(size, default);
        }
        catch (TextureAccelerationException error) when (!required)
        {
            Console.WriteLine("  Metal unavailable on this host: " + error.Message);
            return null;
        }
    }

    private TextureMask[] Masks(TexturePatch patch, double scale, int gutter)
    {
        return Enumerable.Range(0, 4).Select(rotation => TextureMaskBuilder.Build(patch, scale, 1, gutter, rotation, default)).ToArray();
    }

    private TextureMask SolidMask(int width, int height)
    {
        return new TextureMask(width, height, Enumerable.Range(0, height).Select(y => new TextureMaskSpan(y, 0, width)).ToArray(), 0);
    }

    private TexturePatch[] CavityFixture()
    {
        var large = new TexturePatch { Bounds = new SKRect(0, 0, 100, 100) };
        AddRectangle(large, 0, 0, 100, 10);
        AddRectangle(large, 0, 10, 10, 90);
        AddRectangle(large, 0, 90, 100, 100);
        return new[] { large }.Concat(Enumerable.Range(0, 36).Select(_ => Rectangle(9, 9))).ToArray();
    }

    private TexturePatch Rectangle(float width, float height)
    {
        var patch = new TexturePatch { Bounds = new SKRect(0, 0, width, height) };
        AddRectangle(patch, 0, 0, width, height);
        return patch;
    }

    private void AddRectangle(TexturePatch patch, double left, double top, double right, double bottom)
    {
        var start = patch.Coordinates.Count;
        patch.Coordinates.Add(start, [left, top]);
        patch.Coordinates.Add(start + 1, [right, top]);
        patch.Coordinates.Add(start + 2, [right, bottom]);
        patch.Coordinates.Add(start + 3, [left, bottom]);
        patch.Triangles.Add([start, start + 1, start + 2]);
        patch.Triangles.Add([start, start + 2, start + 3]);
    }

    private void ValidatePixels(IReadOnlyList<TexturePatch> patches, int size, int gutter, double scale)
    {
        // Independent subpixel triangle rasterization verifies the final UVs,
        // rather than reusing the mask builder under test for overlap checks.
        var owners = Enumerable.Repeat(-1, size * size).ToArray();
        for (var index = 0; index < patches.Count; index++)
        {
            var patch = patches[index];
            Check(patch.AtlasBounds.Left >= gutter && patch.AtlasBounds.Top >= gutter, "Chart keeps its leading edge gutter");
            Check(patch.AtlasBounds.Right <= size - gutter && patch.AtlasBounds.Bottom <= size - gutter, "Chart keeps its trailing edge gutter");
            foreach (var face in patch.Triangles)
            {
                var points = face.Select(vertex => patch.Coordinate(vertex, patch.QuarterTurns))
                    .Select(point => new TextureCoordinate(patch.AtlasBounds.Left + point.X * scale, patch.AtlasBounds.Top + point.Y * scale)).ToArray();
                for (var y = (int)Math.Floor(points.Min(point => point.Y)); y < Math.Ceiling(points.Max(point => point.Y)); y++)
                {
                    for (var x = (int)Math.Floor(points.Min(point => point.X)); x < Math.Ceiling(points.Max(point => point.X)); x++)
                    {
                        var covered = Inside(points, x + .25, y + .25) || Inside(points, x + .75, y + .25)
                            || Inside(points, x + .25, y + .75) || Inside(points, x + .75, y + .75);
                        if (!covered)
                        {
                            continue;
                        }
                        for (var dy = -gutter; dy <= gutter; dy++)
                        {
                            for (var dx = -gutter; dx <= gutter; dx++)
                            {
                                var target = (y + dy) * size + x + dx;
                                Check(owners[target] == -1 || owners[target] == index, "Independent padded triangle rasters do not overlap");
                                owners[target] = index;
                            }
                        }
                    }
                }
            }
        }
    }

    private bool Inside(TextureCoordinate[] triangle, double x, double y)
    {
        var positive = false;
        var negative = false;
        for (var edge = 0; edge < 3; edge++)
        {
            var a = triangle[edge];
            var b = triangle[(edge + 1) % 3];
            var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
            positive |= cross > 0;
            negative |= cross < 0;
        }
        return !positive || !negative;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
