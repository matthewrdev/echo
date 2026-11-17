using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Meshing;

[Export(typeof(IMeshBuilder)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MeshBuilder : IMeshBuilder
{
    private readonly IComputeService? compute;

    [ImportingConstructor]
    public MeshBuilder([Import(AllowDefault = true)] IComputeService? compute = null)
    {
        this.compute = compute;
    }

    public Surface Extract(FusedVolume volume, CancellationToken cancellation, WorkBuffers? work = null, Action<string>? progress = null, Action<OperationProgress>? operationProgress = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        using var scratch = work?.CreateScope();
        var samples = VolumeGapRepair.Complete(volume, cancellation, scratch, progress);
        var result = TetrahedralSurface.Extract(samples, volume.VoxelSize, cancellation, work, progress, operationProgress, compute, backend);
        if (result.Faces.Count == 0)
        {
            throw new EchoException("No connected surface could be extracted from the fused cloud.", 5);
        }
        scratch?.Dispose();
        SurfaceHoleRepair.Fill(result, volume.VoxelSize, cancellation, work: work, progress: operationProgress);
        return SurfaceFragments.RemoveSmall(result, volume.VoxelSize, cancellation, work);
    }

    public Surface AssignCameras(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        Surface geometry,
        CancellationToken cancellation, Action<string>? progress = null, bool retainUnobserved = false, Action<OperationProgress>? operationProgress = null, WorkBuffers? work = null, bool forCompletion = false, MeshDepthContext? depthContext = null)
    {
        depthContext ??= new MeshDepthContext(project, scene, maps);
        var photos = depthContext.Photos;
        var views = depthContext.Views;
        using var scratch = work?.CreateScope();
        var result = work?.Surface() ?? new Surface();
        var vertices = new BinaryHashIndex<ChartVertex, int>(geometry.Vertices.Count, new ChartVertexCodec(), new IntRecordCodec(), scratch, cancellation);
        var reader = new SurfaceGeometryReader(geometry);
        var writer = new SurfaceGeometryWriter(result);
        Span<int> output = stackalloc int[3];
        var reporter = new PhotographSelectionProgress(progress, operationProgress);
        var selection = FaceViewSelection.Select(geometry, views, photos, cancellation, selectionProgress: reporter, work: work,
            compute: compute, backend: project.Settings.ComputeBackend, optimizeSeams: !forCompletion);
        reporter.Start(PhotographSelectionPhase.AssembleMesh, geometry.Faces.Count);
        for (var faceIndex = 0; faceIndex < geometry.Faces.Count; faceIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (selection[faceIndex] < 0 && !retainUnobserved)
            {
                reporter.Increment();
                continue;
            }
            var face = reader.Face(faceIndex);
            // An empty source means inferred colour, not a photograph through
            // an occluder. Completed geometry must survive missing imagery.
            var image = selection[faceIndex] < 0 ? string.Empty : views[selection[faceIndex]].Camera.ImageId;

            for (var i = 0; i < 3; i++)
            {
                var key = new ChartVertex(face[i], forCompletion ? string.Empty : image);
                if (!vertices.TryGetValue(key, out var index))
                {
                    index = result.Vertices.Count;
                    vertices[key] = index;
                    writer.AddVertex(reader.Vertex(face[i]));
                }

                output[i] = index;
            }

            writer.AddFace(new SurfaceTriangleVertices(output[0], output[1], output[2]));
            result.SourceImages.Add(image);
            reporter.Increment();
        }
        reporter.Complete();

        if (!selection.Any(view => view >= 0))
        {
            throw new EchoException("Too little fused surface has a verified source photograph.", 5);
        }

        return result;
    }

    public SurfaceHoleRepairResult RepairFinalHoles(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps,
        Surface geometry, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? operationProgress = null, MeshDepthContext? depthContext = null)
    {
        ValidateRepair(geometry, voxelSize, cancellation);
        var views = (depthContext ?? new MeshDepthContext(project, scene, maps)).Views;
        var originalFaces = geometry.Faces.Count;
        // The final pass repairs only bounded, nearly planar internal loops.
        // It cannot connect fragments or extrapolate the scan perimeter.
        var repaired = SurfaceHoleRepair.Fill(geometry, voxelSize, cancellation,
            acceptsTriangle: triangle => !SurfaceVisibilityEvidence.Contradicts(triangle, views, project.Settings.CloudDepthTolerance), work: work, progress: operationProgress);
        return new SurfaceHoleRepairResult(repaired, geometry.Faces.Count - originalFaces);
    }

    public void FillHoles(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps,
        Surface geometry, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? operationProgress = null, MeshDepthContext? depthContext = null)
    {
        ValidateRepair(geometry, voxelSize, cancellation);
        var views = (depthContext ?? new MeshDepthContext(project, scene, maps)).Views;
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
        GroundSurfaceCompletion.Fill(geometry, voxelSize, Observed, cancellation, work);
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
