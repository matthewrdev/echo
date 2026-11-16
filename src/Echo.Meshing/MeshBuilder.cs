using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Meshing;

[Export(typeof(IMeshBuilder)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MeshBuilder : IMeshBuilder
{
    [ImportingConstructor]
    public MeshBuilder()
    {
    }

    public Surface Extract(FusedVolume volume, CancellationToken cancellation, WorkBuffers? work = null, Action<string>? progress = null, Action<OperationProgress>? operationProgress = null)
    {
        var samples = VolumeGapRepair.Complete(volume, cancellation, work, progress);
        var result = TetrahedralSurface.Extract(samples, volume.VoxelSize, cancellation, work, progress, operationProgress);
        if (result.Faces.Count == 0)
        {
            throw new EchoException("No connected surface could be extracted from the fused cloud.", 5);
        }
        SurfaceHoleRepair.Fill(result, volume.VoxelSize, cancellation, work: work, progress: operationProgress);
        return SurfaceFragments.RemoveSmall(result, volume.VoxelSize, cancellation, work);
    }

    public Surface AssignCameras(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        Surface geometry,
        CancellationToken cancellation, Action<string>? progress = null, bool retainUnobserved = false)
    {
        var photos = project.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var views = MeshDepthViews.Build(project, scene, maps);
        var result = new Surface();
        var vertices = new Dictionary<ChartVertex, int>();
        var selection = FaceViewSelection.Select(geometry, views, photos, cancellation, progress);
        for (var faceIndex = 0; faceIndex < geometry.Faces.Count; faceIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (selection[faceIndex] < 0 && !retainUnobserved)
            {
                continue;
            }
            var face = geometry.Faces[faceIndex];
            // An empty source means inferred colour, not a photograph through
            // an occluder. Completed geometry must survive missing imagery.
            var image = selection[faceIndex] < 0 ? string.Empty : views[selection[faceIndex]].Camera.ImageId;

            var output = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var key = new ChartVertex(face[i], image);
                if (!vertices.TryGetValue(key, out var index))
                {
                    index = result.Vertices.Count;
                    vertices[key] = index;
                    result.Vertices.Add(geometry.Vertices[face[i]]);
                }

                output[i] = index;
            }

            result.Faces.Add(output);
            result.SourceImages.Add(image);
        }

        if (!selection.Any(view => view >= 0))
        {
            throw new EchoException("Too little fused surface has a verified source photograph.", 5);
        }

        return result;
    }

    public SurfaceHoleRepairResult RepairFinalHoles(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps,
        Surface geometry, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? operationProgress = null)
    {
        ValidateRepair(geometry, voxelSize, cancellation);
        var views = MeshDepthViews.Build(project, scene, maps);
        var originalFaces = geometry.Faces.Count;
        // The final pass repairs only bounded, nearly planar internal loops.
        // It cannot connect fragments or extrapolate the scan perimeter.
        var repaired = SurfaceHoleRepair.Fill(geometry, voxelSize, cancellation,
            acceptsTriangle: triangle => !SurfaceVisibilityEvidence.Contradicts(triangle, views, project.Settings.CloudDepthTolerance), work: work, progress: operationProgress);
        return new SurfaceHoleRepairResult(repaired, geometry.Faces.Count - originalFaces);
    }

    public void FillHoles(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps,
        Surface geometry, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? operationProgress = null)
    {
        ValidateRepair(geometry, voxelSize, cancellation);
        var views = MeshDepthViews.Build(project, scene, maps);
        bool Observed(V3 point)
        {
            var count = 0;
            foreach (var view in views)
            {
                var distance = view.Difference(point, out var cameraDepth);
                var tolerance = Math.Max(voxelSize * 2, cameraDepth * project.Settings.CloudDepthTolerance * 2);
                // Every contributing depth pixel must agree. A bilinear mean
                // between foreground and background is not a measured surface.
                var agrees = double.IsFinite(distance) && Math.Abs(distance) <= tolerance
                    && view.TrySurfacePoint(point, tolerance / cameraDepth, out _);
                if (agrees)
                {
                    count++;
                    if (count >= 2)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        GroundSurfaceCompletion.Fill(geometry, voxelSize, Observed, cancellation);
        SurfaceHoleRepair.Fill(geometry, voxelSize, cancellation, Observed, work: work, progress: operationProgress);
    }

    private void ValidateRepair(Surface geometry, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? operationProgress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!(voxelSize > 0) || !double.IsFinite(voxelSize))
        {
            throw new ArgumentOutOfRangeException(nameof(voxelSize));
        }
        if (geometry.SourceImages.Count != 0)
        {
            throw new EchoException("Complete surface geometry before assigning texture photographs.", 5);
        }
    }
}
