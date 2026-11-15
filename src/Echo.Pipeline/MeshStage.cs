using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Meshing;
using Echo.Simplification;
using Echo.SurfaceCompletion;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MeshStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IMeshBuilder meshBuilder;
    private readonly IEdgeCollapse edgeCollapse;
    private readonly IPlyWriter plyWriter;
    private readonly ISurfaceReconstructor surfaceReconstructor;

    [ImportingConstructor]
    public MeshStage(IFileOperations fileOperations,
        IJsonCodec jsonCodec,
        IMeshBuilder meshBuilder,
        IEdgeCollapse edgeCollapse,
        IPlyWriter plyWriter,
        ISurfaceReconstructor surfaceReconstructor)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.meshBuilder = meshBuilder;
        this.edgeCollapse = edgeCollapse;
        this.plyWriter = plyWriter;
        this.surfaceReconstructor = surfaceReconstructor;
    }

    public string Name => "mesh";
    public int Order => 6;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var alignment = context.Artifact("alignment");
        var scene = context.Scene();
        var depthUnits = context.DepthUnits();
        var maps = context.DepthMaps();
        var fusionPath = context.Artifact("fusion");
        var meshPath = context.Build("mesh", new
        {
            fusionPath,
            depthUnits,
            alignment,
            completionVersion = 4,
            project.Settings.FaceLimit,
            project.Settings.MeshIslandMode,
            project.Settings.MeshIslandMinimumAreaPercent
        }, dir =>
                {
                    var volume = jsonCodec.Read<FusedVolume>(store.PathFor(fusionPath + "/volume.json"));
                    var extracted = meshBuilder.Extract(volume, cancellation);
                    var detailTolerance = volume.VoxelSize * .15;
                    var reduced = edgeCollapse.Reduce(extracted, project.Settings.FaceLimit, cancellation, detailTolerance);
                    meshBuilder.FillHoles(project, scene, maps, reduced, volume.VoxelSize, cancellation);
                    if (reduced.Faces.Count > project.Settings.FaceLimit)
                    {
                        reduced = edgeCollapse.Reduce(reduced, project.Settings.FaceLimit, cancellation);
                    }
                    context.ReportProgress(Name, "Removing surfaces contradicted by visible depth rays before completion…");
                    var facesBeforeVisibility = reduced.Faces.Count;
                    reduced = SurfaceVisibilityFilter.Filter(reduced, project, scene, maps, cancellation);
                    var rejectedBeforeCompletion = facesBeforeVisibility - reduced.Faces.Count;
                    var rejectedAfterCompletion = 0;
                    context.ReportProgress(Name, $"Rejected {rejectedBeforeCompletion:N0} triangles contradicted by observed empty space.");
                    var completed = SurfaceSupportFilter.HasGaps(reduced, cancellation);
                    if (completed)
                    {
                        var observed = meshBuilder.AssignCameras(project, scene, maps, reduced, cancellation,
                            message => context.ReportProgress(Name, message));
                        var reconstructed = surfaceReconstructor.Reconstruct(observed, scene, volume.VoxelSize, cancellation,
                            message => context.ReportProgress(Name, message));
                        context.ReportProgress(Name, "Restricting surface completion to the scanned neighbourhood…");
                        var supported = SurfaceSupportFilter.Filter(reconstructed, observed, volume.VoxelSize * 24, cancellation);
                        context.ReportProgress(Name, "Adapting the completed surface to its geometric detail…");
                        reduced = edgeCollapse.Reduce(supported, project.Settings.FaceLimit, cancellation, detailTolerance);
                        context.ReportProgress(Name, "Checking completed surfaces against observed empty space…");
                        facesBeforeVisibility = reduced.Faces.Count;
                        reduced = SurfaceVisibilityFilter.Filter(reduced, project, scene, maps, cancellation);
                        rejectedAfterCompletion = facesBeforeVisibility - reduced.Faces.Count;
                        context.ReportProgress(Name, $"Rejected {rejectedAfterCompletion:N0} completed triangles contradicted by observed empty space.");
                    }
                    context.ReportProgress(Name, "Repairing small enclosed holes after visibility cleanup…");
                    var finalRepair = meshBuilder.RepairFinalHoles(project, scene, maps, reduced, volume.VoxelSize, cancellation);
                    context.ReportProgress(Name, $"Closed {finalRepair.RepairedHoles:N0} small holes with {finalRepair.AddedFaces:N0} triangles.");
                    if (reduced.Faces.Count > project.Settings.FaceLimit)
                    {
                        reduced = edgeCollapse.Reduce(reduced, project.Settings.FaceLimit, cancellation);
                    }
                    var mesh = meshBuilder.AssignCameras(project, scene, maps, reduced, cancellation,
                        message => context.ReportProgress(Name, message), retainUnobserved: true);
                    if (project.Settings.MeshIslandMode != IslandTrimMode.Off)
                    {
                        context.ReportProgress(Name, "Trimming disconnected islands after surface completion…");
                    }
                    var trimming = SurfaceIslandTrimmer.Trim(mesh, project.Settings.MeshIslandMode,
                        project.Settings.MeshIslandMinimumAreaPercent, cancellation);
                    mesh = trimming.Surface;
                    if (trimming.InputComponents.HasValue)
                    {
                        context.ReportProgress(Name, $"Removed {trimming.RemovedComponents:N0} disconnected islands ({trimming.RemovedFaces:N0} triangles).");
                    }
                    fileOperations.AtomicJson(Path.Combine(dir, "mesh.json"), mesh);
                    plyWriter.Write(Path.Combine(dir, "mesh.ply"), mesh.Vertices.ToArray(), mesh.Faces.ToArray());
                    fileOperations.AtomicJson(Path.Combine(dir, "mesh.report.json"), new
                    {
                        extractedFaces = extracted.Faces.Count,
                        reducedFaces = reduced.Faces.Count,
                        texturedFaces = mesh.Faces.Count,
                        inferredColourFaces = mesh.SourceImages.Count(string.IsNullOrEmpty),
                        requestedFaceLimit = project.Settings.FaceLimit,
                        volume.VoxelSize,
                        detailTolerance,
                        completed,
                        visibilityFiltering = new
                        {
                            rejectedBeforeCompletion,
                            rejectedAfterCompletion
                        },
                        finalHoleRepair = finalRepair,
                        completionDistance = completed ? volume.VoxelSize * 24 : 0,
                        islandTrimming = new
                        {
                            mode = project.Settings.MeshIslandMode,
                            minimumAreaPercent = project.Settings.MeshIslandMinimumAreaPercent,
                            trimming.InputComponents,
                            trimming.RemovedComponents,
                            trimming.RemovedFaces,
                            trimming.RemovedArea
                        },
                        method = "Visibility-constrained screened Poisson completion, adaptive simplification and graph-cut photographic view selection",
                        note = "Completed regions are inferred geometry. Distant extrapolation and surfaces contradicted by multiple visible depth rays are removed; missing and occluded depths remain neutral."
                    });
                });
        context.Publish("mesh", meshPath);
    }
}
