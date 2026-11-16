using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Meshing;
using Echo.Storage;

namespace Echo.Tests;

[Export]
public sealed class ExclusionRegionTests
{
    private readonly IJsonCodec json;
    private readonly IComputeService compute;

    [ImportingConstructor]
    public ExclusionRegionTests(IJsonCodec json, IComputeService compute)
    {
        this.json = json;
        this.compute = compute;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Box and cylinder exclusions include boundaries and respect height", Geometry);
        tests.Run("Exclusion settings survive snapshots and reject invalid input", SettingsRoundTrip);
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

    private void Geometry()
    {
        var box = new ExclusionRegion { CentreX = 5, CentreY = 2, CentreZ = -3, Width = 4, Height = 2, Depth = 6 };
        Check(ExclusionGeometry.Contains([box], new V3(7, 3, 0)), "Box boundary must be excluded");
        Check(!ExclusionGeometry.Contains([box], new V3(7.001, 3, 0)), "Outside point was excluded");
        box.Enabled = false;
        Check(!ExclusionGeometry.Contains([box], new V3(5, 2, -3)), "Disabled region must be ignored");
        var cylinder = new ExclusionRegion { Shape = ExclusionShape.Cylinder, Radius = 2, Height = 4 };
        Check(ExclusionGeometry.Contains([cylinder], new V3(0, 2, 2)), "Cylinder cap and radial boundary must be excluded");
        Check(!ExclusionGeometry.Contains([cylinder], new V3(1.5, 0, 1.5)), "Cylinder must not behave like its bounding box");
        Check(!ExclusionGeometry.Contains([cylinder], new V3(0, 2.001, 0)), "Cylinder height must be finite");
        Check(ExclusionGeometry.Intersects(cylinder, new V3(-4, 0, -4), new V3(4, 0, -4), new V3(0, 0, 4)),
            "A large triangle covering the cylinder must be removed");
        Check(!ExclusionGeometry.Intersects(cylinder, new V3(-4, 3, -4), new V3(4, 3, -4), new V3(0, 3, 4)),
            "Triangle above the cap must remain");
        Check(ExclusionGeometry.Intersects(cylinder, new V3(0, -5, -5), new V3(0, 5, -5), new V3(0, 0, 5)),
            "Vertical triangle crossing the cylinder must be removed");
        Check(!ExclusionGeometry.Intersects(cylinder, new V3(1.8, -1, 1.8), new V3(3, -1, 1.8), new V3(1.8, 1, 3)),
            "Triangle outside the curved wall must remain");
    }

    private void SettingsRoundTrip()
    {
        var settings = new Settings { ExclusionRegions = [new ExclusionRegion(), new ExclusionRegion { Shape = ExclusionShape.Cylinder }] };
        var restored = json.Clone(settings);
        restored.Validate();
        Check(restored.ExclusionRegions.Count == 2 && restored.ExclusionRegions[1].Shape == ExclusionShape.Cylinder,
            "Regions did not survive the serialized run snapshot");
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
        Check(source.DepthAt(centre) == 10, "Saved depth artifacts must remain reusable");
        Check(ReferenceEquals(source, DepthExclusions.Apply(fixture.Project.Images[0], fixture.Scene.Cameras[0], source, [])),
            "Projects without exclusions must keep the existing path");
    }

    private void Fusion(ComputeBackend backend)
    {
        foreach (var shape in Enum.GetValues<ExclusionShape>())
        {
            var fixture = new FusionFixture();
            var region = new ExclusionRegion { Shape = shape, CentreZ = 10, Width = 1, Height = 1, Depth = 1, Radius = .5 };
            fixture.Project.Settings.ExclusionRegions = [region];
            fixture.Project.Settings.ComputeBackend = backend;
            fixture.Project.Settings.FusionVoxelSize = .125;
            var fusion = new DepthFusion(compute);
            var volume = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud,
                new FusionResourcePlan(25600000, 100000), default);
            Check(volume.Samples.Count >= 100 && volume.ConsistentSamples < fixture.Cloud.Samples.Count,
                "Excluded cloud points still affected fusion planning");
            Check(volume.Samples.All(sample => !ExclusionGeometry.Contains([region],
                new V3(sample.X * volume.VoxelSize, sample.Y * volume.VoxelSize, sample.Z * volume.VoxelSize))),
                "Excluded volume samples survived integration");
            Check(fixture.Maps.All(map => map.Depth.All(value => value == 10)), "Fusion mutated source depths");
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
                    var filtered = SurfaceExclusionFilter.Filter(surface, [region], default, buffers);
                    Check(filtered.Faces.Count == 1 && filtered.Vertices.Count == 3, "Crossing triangle or unused vertices survived");
                    Check(filtered.SourceImages.SequenceEqual(["retained"]), "Source photograph mapping shifted");
                    Check(filtered.Faces[0].SequenceEqual([0, 1, 2]), "Compacted vertex indices are invalid");
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
