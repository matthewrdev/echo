using Echo.Models;
using Echo.Export;
using Echo.Infrastructure;
using Echo.Meshing;
using Echo.Projects;
using Echo.Storage;

namespace Echo.Validation;

public sealed class ReconstructionMeasurements : IReconstructionMeasurements
{
    private readonly IBulkArtifactStore bulk;
    private readonly IJsonCodec json;
    private readonly IGlbWriter glb;

    public ReconstructionMeasurements(IBulkArtifactStore bulk, IJsonCodec json, IGlbWriter glb)
    {
        this.bulk = bulk;
        this.json = json;
        this.glb = glb;
    }

    public Dictionary<QualityMetric, double> Read(IProjectStore store, RunData run, CancellationToken cancellation)
    {
        if (run.Status != "completed" || run.Target != "export")
        {
            throw new InvalidDataException("Quality validation requires a completed export run.");
        }
        string Artifact(string stage, string file) => ValidationPaths.ContainedPath(store.Root, run.Artifacts[stage] + "/" + file);
        using var session = bulk.OpenSession(32 * 1024 * 1024, 0);
        var scene = session.Read<ReconstructionScene>(Artifact("alignment", "scene.json"));
        var enabled = run.Snapshot.Images.Count(photo => photo.Enabled && !photo.Discarded);
        if (enabled == 0 || scene.Bundle == null)
        {
            throw new InvalidDataException("Missing enabled photographs or bundle adjustment measurements.");
        }
        var result = new Dictionary<QualityMetric, double>
        {
            [QualityMetric.RegisteredCameraFraction] = (double)scene.Cameras.Count / enabled,
            [QualityMetric.MedianReprojectionErrorPixels] = scene.Bundle.FinalMedianError,
            [QualityMetric.SparsePoints] = scene.Points.Count
        };
        ReadDepth(store, run, scene, result, cancellation);
        var cloud = json.Read<DenseCloudReport>(Artifact("cloud", "cloud.json"));
        result[QualityMetric.DenseSamples] = cloud.RetainedSamples;
        var mesh = session.Read<Surface>(Artifact("mesh", "mesh.json"));
        ReadMesh(mesh, result, cancellation);
        var topology = SurfaceTopology.Measure(mesh, cancellation);
        result[QualityMetric.MainHullBoundaryEdges] = topology.BoundaryEdges;
        result[QualityMetric.MainHullInteriorHoles] = topology.InteriorHoles;
        result[QualityMetric.MainHullNonManifoldEdges] = topology.NonManifoldEdges;
        result[QualityMetric.MainHullWindingConflicts] = topology.WindingConflicts;
        result[QualityMetric.MainHullDegenerateFaces] = topology.DegenerateFaces;
        var export = Artifact("export", "cliff.glb");
        glb.Inspect(export);
        result[QualityMetric.ExportBytes] = new FileInfo(export).Length;
        return result;
    }

    public SurfaceTopologyReport Topology(IProjectStore store, RunData run, CancellationToken cancellation)
    {
        using var session = bulk.OpenSession(32 * 1024 * 1024, 0);
        var mesh = session.Read<Surface>(ValidationPaths.ContainedPath(store.Root, run.Artifacts["mesh"] + "/mesh.json"));
        return SurfaceTopology.Measure(mesh, cancellation);
    }

    private void ReadDepth(IProjectStore store, RunData run, ReconstructionScene scene,
        Dictionary<QualityMetric, double> result, CancellationToken cancellation)
    {
        var indexPath = ValidationPaths.ContainedPath(store.Root, run.Artifacts["depth"] + "/units.json");
        var units = json.Read<List<string>>(indexPath);
        var expected = scene.Cameras.Select(camera => camera.ImageId).ToHashSet(StringComparer.Ordinal);
        var measured = new HashSet<string>(StringComparer.Ordinal);
        var coverage = new List<double>();
        foreach (var unit in units)
        {
            cancellation.ThrowIfCancellationRequested();
            using var session = bulk.OpenSession(1024 * 1024, 0);
            var map = session.Read<DepthMap>(ValidationPaths.ContainedPath(store.Root, unit + "/depth.json"));
            if (!expected.Contains(map.ImageId) || !measured.Add(map.ImageId))
            {
                throw new InvalidDataException("Depth map does not belong to its registered camera.");
            }
            var accepted = 0;
            for (var index = 0; index < map.SampleCount; index++)
            {
                var value = map.DepthAt(index);
                var confidence = map.ConfidenceAt(index);
                var valid = float.IsFinite(value) && float.IsFinite(confidence)
                    && value >= 0 && confidence >= 0 && confidence <= 1;
                if (!valid)
                {
                    throw new InvalidDataException("Depth maps contain invalid samples.");
                }
                if (value > 0)
                {
                    accepted++;
                }
            }
            coverage.Add((double)accepted / map.SampleCount);
        }
        if (coverage.Count == 0 || !measured.SetEquals(expected))
        {
            throw new InvalidDataException("No depth maps were measured.");
        }
        coverage.Sort();
        result[QualityMetric.MeanDepthCoverage] = coverage.Average();
        result[QualityMetric.LowerDecileDepthCoverage] = coverage[(coverage.Count - 1) / 10];
    }

    private void ReadMesh(Surface mesh, Dictionary<QualityMetric, double> result, CancellationToken cancellation)
    {
        if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0 || mesh.SourceImages.Count != mesh.Faces.Count)
        {
            throw new InvalidDataException("Exported surface must contain vertices, faces and photographic assignments.");
        }
        var minimum = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var maximum = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        foreach (var vertex in mesh.Vertices)
        {
            if (vertex.Length != 3 || vertex.Any(value => !double.IsFinite(value)))
            {
                throw new InvalidDataException("Surface contains invalid vertices.");
            }
            for (var axis = 0; axis < 3; axis++)
            {
                minimum[axis] = Math.Min(minimum[axis], vertex[axis]);
                maximum[axis] = Math.Max(maximum[axis], vertex[axis]);
            }
        }
        foreach (var face in mesh.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
            if (face.Length != 3 || face.Any(index => index < 0 || index >= mesh.Vertices.Count))
            {
                throw new InvalidDataException("Surface contains invalid triangle indices.");
            }
        }
        var coverage = SurfacePhotographCoverage.Measure(mesh, cancellation);
        result[QualityMetric.MeshFaces] = mesh.Faces.Count;
        result[QualityMetric.MeshArea] = coverage.Area;
        result[QualityMetric.InferredColourAreaFraction] = coverage.InferredArea / coverage.Area;
        result[QualityMetric.PhotographedColourArea] = coverage.Area - coverage.InferredArea;
        result[QualityMetric.MeshExtentX] = maximum[0] - minimum[0];
        result[QualityMetric.MeshExtentY] = maximum[1] - minimum[1];
        result[QualityMetric.MeshExtentZ] = maximum[2] - minimum[2];
    }
}
