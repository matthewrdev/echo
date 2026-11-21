using Echo.Compute;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Meshing.Visibility;
using Echo.Meshing;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Tests;

public sealed class ExclusionRegionTests
{
    private readonly IJsonCodec json;
    private readonly IComputeService compute;

    public ExclusionRegionTests(IJsonCodec json, IComputeService compute)
    {
        this.json = json;
        this.compute = compute;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Box and cylinder exclusions include boundaries and respect height", Geometry);
        tests.Run("Include regions form a union with exclusions taking precedence", Inclusion);
        tests.Run("Rotated regions share local coordinates for points and triangles", Rotation);
        tests.Run("Exclusion settings survive snapshots and reject invalid input", SettingsRoundTrip);
        tests.Run("Painted hulls preserve projection, modes and serialized settings", PaintedHull);
        tests.Run("Depth exclusions mask scalar and bulk reads without modifying saved depths", DepthMask);
        tests.Run("Fusion excludes raw depth evidence and volume samples on CPU", () => Fusion(ComputeBackend.Cpu));
        bool accelerated;
        using (var probe = compute.Open(ComputeBackend.Auto, default))
        {
            accelerated = probe != null;
        }
        if (accelerated)
        {
            tests.Run("Fusion excludes raw depth evidence and volume samples on Metal", () => Fusion(ComputeBackend.Metal));
        }
        tests.Run("Mesh exclusions remove crossing triangles even with all vertices outside", Mesh);
    }

    private void PaintedHull()
    {
        var region = new ExclusionRegion
        {
            Shape = ExclusionShape.ProjectedHull,
            Mode = CropRegionMode.Include,
            Hull = CropHullFixture.Box(new V3(0, 0, 0), new V3(2, 2, 6))
        };
        var restored = json.Clone(new Settings { ExclusionRegions = [region] });
        restored.Validate();
        var filter = new RegionCropFilter(restored.ExclusionRegions);
        Check(!filter.IsExcluded(new V3(0, 0, -2)) && !filter.IsExcluded(new V3(0, 0, 2)), "Projected crop must include the entire selected depth");
        Check(filter.IsExcluded(new V3(2, 0, 0)) && filter.IsExcluded(new V3(0, 0, 4)), "Crop must respect polygon sides and depth caps");
        region.Mode = CropRegionMode.Exclude;
        filter = new RegionCropFilter([region]);
        Check(filter.ExcludesTriangle(new V3(-5, -5, 0), new V3(5, -5, 0), new V3(0, 5, 0)), "Spanning triangles must intersect the painted volume");
        Check(!filter.ExcludesTriangle(new V3(-5, -5, 4), new V3(5, -5, 4), new V3(0, 5, 4)), "Triangles beyond the depth cap must survive");
        Check(!json.Encode(new ExclusionRegion()).Contains("hull", StringComparison.OrdinalIgnoreCase), "Legacy region payloads must retain their fingerprints");
        restored.ExclusionRegions[0].Hull!.Planes[0].NormalX = double.NaN;
        Reject(restored);
        var native = Environment.GetEnvironmentVariable("ECHO_TEST_PAINT_SETTINGS");
        if (native != null)
        {
            var settings = json.Read<Settings>(native);
            settings.Validate();
            var hull = settings.ExclusionRegions.Single().Hull!;
            var centre = hull.Vertices.Select(vertex => new V3(vertex[0], vertex[1], vertex[2])).Aggregate(new V3(0, 0, 0), (a, b) => a + b) / hull.Vertices.Length;
            Check(!new RegionCropFilter(settings.ExclusionRegions).IsExcluded(centre), "Native painted hull was not retained by processing");
        }
    }

    private void Geometry()
    {
        var box = new ExclusionRegion { CentreX = 5, CentreY = 2, CentreZ = -3, Width = 4, Height = 2, Depth = 6 };
        Check(new RegionCropFilter([box]).IsExcluded(new V3(7, 3, 0)), "Box boundary must be excluded");
        Check(!new RegionCropFilter([box]).IsExcluded(new V3(7.001, 3, 0)), "Outside point was excluded");
        box.Enabled = false;
        Check(!new RegionCropFilter([box]).IsExcluded(new V3(5, 2, -3)), "Disabled region must be ignored");
        var cylinder = new ExclusionRegion { Shape = ExclusionShape.Cylinder, Radius = 2, Height = 4 };
        Check(new RegionCropFilter([cylinder]).IsExcluded(new V3(0, 2, 2)), "Cylinder cap and radial boundary must be excluded");
        Check(!new RegionCropFilter([cylinder]).IsExcluded(new V3(1.5, 0, 1.5)), "Cylinder must not behave like its bounding box");
        Check(!new RegionCropFilter([cylinder]).IsExcluded(new V3(0, 2.001, 0)), "Cylinder height must be finite");
        Check(ExclusionGeometry.Intersects(cylinder, new V3(-4, 0, -4), new V3(4, 0, -4), new V3(0, 0, 4)),
            "A large triangle covering the cylinder must be removed");
        Check(!ExclusionGeometry.Intersects(cylinder, new V3(-4, 3, -4), new V3(4, 3, -4), new V3(0, 3, 4)),
            "Triangle above the cap must remain");
        Check(ExclusionGeometry.Intersects(cylinder, new V3(0, -5, -5), new V3(0, 5, -5), new V3(0, 0, 5)),
            "Vertical triangle crossing the cylinder must be removed");
        Check(!ExclusionGeometry.Intersects(cylinder, new V3(1.8, -1, 1.8), new V3(3, -1, 1.8), new V3(1.8, 1, 3)),
            "Triangle outside the curved wall must remain");
    }

    private void Inclusion()
    {
        var include = new ExclusionRegion { Mode = CropRegionMode.Include, Width = 4, Height = 4, Depth = 4 };
        var second = new ExclusionRegion { Mode = CropRegionMode.Include, CentreX = 5, Width = 2, Height = 2, Depth = 2 };
        var exclude = new ExclusionRegion { Width = 1, Height = 1, Depth = 1 };
        var filter = new RegionCropFilter([include, second, exclude]);
        Check(!filter.IsExcluded(new V3(2, 2, 2)), "Inclusion boundary must be retained");
        Check(!filter.IsExcluded(new V3(5, 0, 0)), "Include regions must combine as a union");
        Check(filter.IsExcluded(new V3(3, 0, 0)), "Space between include regions must be excluded");
        Check(filter.IsExcluded(new V3(0, 0, 0)), "Exclusions must override inclusions");
        Check(!filter.ExcludesTriangle(new V3(1, 0, 0), new V3(1, 1, 0), new V3(1, 0, 1)), "Interior triangle must survive");
        Check(filter.ExcludesTriangle(new V3(1, 0, 0), new V3(5, 0, 0), new V3(5, 1, 0)), "Triangles must not bridge separated include regions");
        Check(filter.ExcludesTriangle(new V3(-1, -1, 0), new V3(1, -1, 0), new V3(0, 1, 0)), "Exclusion cutouts must survive inside include regions");
        include.Enabled = false;
        second.Enabled = false;
        filter = new RegionCropFilter([include, second, exclude]);
        Check(!filter.IsExcluded(new V3(20, 0, 0)), "Disabling all inclusions must restore outside points");
        Check(!new RegionCropFilter([]).IsExcluded(new V3(0, 0, 0)), "Empty selection must retain all points");
    }

    private void Rotation()
    {
        var box = new ExclusionRegion { Width = 4, Height = .5, Depth = .5, RotationY = 90, CentreX = 5 };
        Check(ExclusionGeometry.Contains(box, new V3(5, 0, -1.5)), "Box must rotate around its vertical centre axis");
        Check(!ExclusionGeometry.Contains(box, new V3(6.5, 0, 0)), "Rotated box retained the original orientation");
        Check(!ExclusionGeometry.Contains(box, new V3(5, .251, -1.5)), "Vertical rotation must preserve the height limits");
        Check(ExclusionGeometry.Intersects(box, new V3(4, -1, -1.5), new V3(6, -1, -1.5), new V3(5, 1, -1.5)),
            "Triangle intersection must use the box rotation");
        box.RotationY = 45;
        Check(ExclusionGeometry.Contains(box, new V3(6.060660171779821, 0, -1.0606601717798212)),
            "Vertical rotation differs from the native viewer");
        var cylinder = new ExclusionRegion { Shape = ExclusionShape.Cylinder, Radius = .25, Height = 4, RotationY = 90 };
        Check(ExclusionGeometry.Contains(cylinder, new V3(0, 1.5, 0)), "Cylinder must stay upright regardless of a saved box rotation");
        Check(!ExclusionGeometry.Contains(cylinder, new V3(1.5, 0, 0)), "Cylinder must retain its radial limits");
        Check(ExclusionGeometry.Intersects(cylinder, new V3(-1, 1, -1), new V3(1, 1, -1), new V3(0, 1, 1)),
            "Cylinder triangle intersection must retain its vertical axis");
    }

    private void SettingsRoundTrip()
    {
        var settings = new Settings { ExclusionRegions = [new ExclusionRegion(), new ExclusionRegion { Shape = ExclusionShape.Cylinder, Mode = CropRegionMode.Include, RotationY = 34 }] };
        var restored = json.Clone(settings);
        restored.Validate();
        Check(restored.ExclusionRegions.Count == 2 && restored.ExclusionRegions[1].Shape == ExclusionShape.Cylinder,
            "Regions did not survive the serialized run snapshot");
        var region = restored.ExclusionRegions[1];
        Check(region.Mode == CropRegionMode.Include && region.RotationY == 34,
            "Mode and rotation did not survive the snapshot");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<ExclusionRegion>("{}");
        Check(legacy?.Mode == CropRegionMode.Exclude && legacy.RotationY == 0, "Old regions must remain unrotated exclusions");
        Check(json.Clone(new Settings()).ExclusionRegions.Count == 0, "No regions must remain the default");
        foreach (var invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            restored.ExclusionRegions[0].Height = invalid;
            Reject(restored);
        }
        restored = json.Clone(settings);
        restored.ExclusionRegions[1].Id = restored.ExclusionRegions[0].Id;
        Reject(restored);
        restored = json.Clone(settings);
        restored.ExclusionRegions[0].CentreX = double.NaN;
        Reject(restored);
        restored = json.Clone(settings);
        restored.ExclusionRegions[0].RotationY = double.PositiveInfinity;
        Reject(restored);
        restored = json.Clone(settings);
        restored.ExclusionRegions[0].Mode = (CropRegionMode)42;
        Reject(restored);
    }

    private void DepthMask()
    {
        var fixture = new FusionFixture();
        var source = fixture.Maps[0];
        var region = new ExclusionRegion { CentreX = -.25, CentreZ = 10, Width = 1, Height = 1, Depth = 1 };
        var masked = DepthExclusions.Apply(fixture.Project.Images[0], fixture.Scene.Cameras[0], source, [region]);
        var centre = 32 * source.Width + 32;
        Check(masked.DepthAt(centre) == 0 && masked.DepthAt(0) == 10, "Depth mask is not in reconstructed world coordinates");
        var bulk = new float[source.SampleCount];
        masked.ReadDepth(0, bulk);
        Check(bulk[centre] == 0 && bulk[0] == 10, "GPU upload and CPU samples must see the same masked values");
        region.Mode = CropRegionMode.Include;
        region.RotationY = 30;
        var included = DepthExclusions.Apply(fixture.Project.Images[0], fixture.Scene.Cameras[0], source, [region]);
        included.ReadDepth(0, bulk);
        Check(included.DepthAt(centre) == 10 && included.DepthAt(0) == 0 && bulk[centre] == 10 && bulk[0] == 0,
            "Inverted depth mask must retain the inside on both CPU and GPU reads");
        var cached = (ExcludedDepthSamples)included.Samples!;
        cached.PrepareMask(default);
        var tile = new float[bulk.Length - 17];
        included.ReadDepth(15, tile);
        Check(bulk.AsSpan(15, tile.Length).SequenceEqual(tile), "Cached masking changed a depth tile");
        Check(included.DepthAt(centre) == 10 && included.DepthAt(0) == 0, "Cached masking changed individual depth reads");
        Check(source.DepthAt(centre) == 10, "Saved depth artifacts must remain reusable");
        Check(ReferenceEquals(source, DepthExclusions.Apply(fixture.Project.Images[0], fixture.Scene.Cameras[0], source, [])),
            "Projects without exclusions must keep the existing path");
    }

    private void Fusion(ComputeBackend backend)
    {
        foreach (var shape in Enum.GetValues<ExclusionShape>())
        {
            foreach (var mode in Enum.GetValues<CropRegionMode>())
            {
                var fixture = new FusionFixture();
                var region = new ExclusionRegion { Shape = shape, Mode = mode, RotationY = 30, CentreZ = 10, Width = 2, Height = 2, Depth = 1, Radius = 1 };
                if (shape == ExclusionShape.ProjectedHull)
                {
                    region.Hull = CropHullFixture.Box(new V3(0, 0, 10), new V3(2, 2, 1));
                }
                fixture.Project.Settings.ExclusionRegions = [region];
                fixture.Project.Settings.ComputeBackend = backend;
                fixture.Project.Settings.FusionVoxelSize = .125;
                var fusion = new DepthFusion(compute);
                var volume = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud,
                    new FusionResourcePlan(25600000, 100000), default);
                Check(volume.Samples.Count >= 100 && volume.ConsistentSamples < fixture.Cloud.Samples.Count,
                    "Excluded cloud points still affected fusion planning");
                Check(volume.Samples.All(sample => !new RegionCropFilter([region]).IsExcluded(
                    new V3(sample.X * volume.VoxelSize, sample.Y * volume.VoxelSize, sample.Z * volume.VoxelSize))),
                    "Excluded volume samples survived integration");
                Check(fixture.Maps.All(map => map.Depth.All(value => value == 10)), "Fusion mutated source depths");
            }
        }
    }

    private void Mesh()
    {
        var surface = new Surface
        {
            Vertices = [[-4, 0, -4], [4, 0, -4], [0, 0, 4], [10, 0, 10], [12, 0, 10], [10, 0, 12]],
            Faces = [[0, 1, 2], [3, 4, 5]],
            SourceImages = ["excluded", "retained"]
        };
        var directory = Path.Combine(Path.GetTempPath(), "echo-exclusions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var data = new BulkDataSession(65536, 0);
            var work = new WorkBuffers(data, directory);
            foreach (var shape in Enum.GetValues<ExclusionShape>())
            {
                foreach (var buffers in new WorkBuffers?[] { null, work })
                {
                    var region = new ExclusionRegion { Shape = shape, Width = 2, Height = 2, Depth = 2, Radius = 1 };
                    if (shape == ExclusionShape.ProjectedHull)
                    {
                        region.Hull = CropHullFixture.Box(new V3(0, 0, 0), new V3(2, 2, 2));
                    }
                    var filtered = SurfaceExclusionFilter.Filter(surface, [region], default, buffers);
                    Check(filtered.Faces.Count == 1 && filtered.Vertices.Count == 3, "Crossing triangle or unused vertices survived");
                    Check(filtered.SourceImages.SequenceEqual(["retained"]), "Source photograph mapping shifted");
                    Check(filtered.Faces[0].SequenceEqual([0, 1, 2]), "Compacted vertex indices are invalid");
                    region.Mode = CropRegionMode.Include;
                    region.CentreX = 11;
                    region.CentreZ = 11;
                    region.Width = 4;
                    region.Depth = 4;
                    region.Radius = 2;
                    region.RotationY = 45;
                    if (shape == ExclusionShape.ProjectedHull)
                    {
                        region.Hull = CropHullFixture.Box(new V3(11, 0, 11), new V3(4, 2, 4));
                    }
                    var included = SurfaceExclusionFilter.Filter(surface, [region], default, buffers);
                    Check(included.Faces.Count == 1 && included.Vertices.Count == 3, "Include region did not remove exterior faces");
                    Check(included.SourceImages.SequenceEqual(["retained"]), "Include region lost source image mapping");
                }
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private void Reject(Settings settings)
    {
        try
        {
            settings.Validate();
        }
        catch (EchoException)
        {
            return;
        }
        throw new InvalidOperationException("Invalid exclusion settings were accepted");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
