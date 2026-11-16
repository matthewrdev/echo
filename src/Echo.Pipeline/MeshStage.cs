using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.SceneSemantics;
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
    private readonly IComputeService? compute;

    [ImportingConstructor]
    public MeshStage(IFileOperations fileOperations,
        IJsonCodec jsonCodec,
        IMeshBuilder meshBuilder,
        IEdgeCollapse edgeCollapse,
        IPlyWriter plyWriter,
        ISurfaceReconstructor surfaceReconstructor, [Import(AllowDefault = true)] IComputeService? compute = null)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.meshBuilder = meshBuilder;
        this.edgeCollapse = edgeCollapse;
        this.plyWriter = plyWriter;
        this.surfaceReconstructor = surfaceReconstructor;
        this.compute = compute;
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
        var fusionPath = context.Artifact("fusion");
        var semanticsPath = project.Settings.OptimiseVegetation ? context.Artifact("semantics") : null;
        var semanticUnits = semanticsPath == null ? new Dictionary<string, string>()
            : context.ReadArtifact<Dictionary<string, string>>("semantics", "units.json");
        var meshPath = context.Build("mesh", new
        {
            fusionPath,
            depthUnits,
            alignment,
            completionVersion = 5,
            project.Settings.ComputeBackend,
            context.ComputeImplementation,
            photographSelectionVersion = 2,
            vegetationVersion = 2,
            semanticsPath,
            project.Settings.OptimiseVegetation,
            project.Settings.VegetationMeshToleranceMultiplier,
            project.Settings.FaceLimit,
            project.Settings.MeshIslandMode,
            project.Settings.MeshIslandMinimumAreaPercent
        }, dir =>
                {
                    var semantics = semanticUnits.ToDictionary(pair => pair.Key,
                        pair => context.ReadData<SemanticMap>(store.PathFor(pair.Value + "/semantics.json")));
                    var maps = context.DepthMaps();
                    var volume = context.ReadData<FusedVolume>(store.PathFor(fusionPath + "/volume.json"));
                    context.BeginOperation(Name, "Surface extraction");
                    var extracted = meshBuilder.Extract(volume, cancellation, context.Work, message => context.ReportProgress(Name, message), update => context.ReportOperation(Name, update), project.Settings.ComputeBackend);
                    var detailTolerance = volume.VoxelSize * .15;
                    context.BeginOperation(Name, "Geometry simplification");
                    var reduced = edgeCollapse.Reduce(extracted, project.Settings.FaceLimit, cancellation, detailTolerance, work: context.Work, progress: message => context.ReportProgress(Name, message));
                    context.BeginOperation(Name, "Observed hole repair");
                    meshBuilder.FillHoles(project, scene, maps, reduced, volume.VoxelSize, cancellation, context.Work, update => context.ReportOperation(Name, update));
                    if (reduced.Faces.Count > project.Settings.FaceLimit)
                    {
                        reduced = edgeCollapse.Reduce(reduced, project.Settings.FaceLimit, cancellation, work: context.Work, progress: message => context.ReportProgress(Name, message));
                    }
                    context.ReportProgress(Name, "Removing surfaces contradicted by visible depth rays before completion…");
                    var facesBeforeVisibility = reduced.Faces.Count;
                    reduced = SurfaceVisibilityFilter.Filter(reduced, project, scene, maps, cancellation, context.Work, update => context.ReportOperation(Name, update), compute);
                    var rejectedBeforeCompletion = facesBeforeVisibility - reduced.Faces.Count;
                    var rejectedAfterCompletion = 0;
                    context.ReportProgress(Name, $"Rejected {rejectedBeforeCompletion:N0} triangles contradicted by observed empty space.");
                    var completed = SurfaceSupportFilter.HasGaps(reduced, cancellation, context.Work);
                    if (completed)
                    {
                        var observed = meshBuilder.AssignCameras(project, scene, maps, reduced, cancellation,
                            message => context.ReportProgress(Name, message));
                        var reconstructed = surfaceReconstructor.Reconstruct(observed, scene, volume.VoxelSize, cancellation,
                            message => context.ReportProgress(Name, message), context.Memory);
                        context.ReportProgress(Name, "Restricting surface completion to the scanned neighbourhood…");
                        var supported = SurfaceSupportFilter.Filter(reconstructed, observed, volume.VoxelSize * 24, cancellation, context.Work, update => context.ReportOperation(Name, update));
                        context.ReportProgress(Name, "Adapting the completed surface to its geometric detail…");
                        reduced = edgeCollapse.Reduce(supported, project.Settings.FaceLimit, cancellation, detailTolerance, work: context.Work, progress: message => context.ReportProgress(Name, message));
                        context.ReportProgress(Name, "Checking completed surfaces against observed empty space…");
                        facesBeforeVisibility = reduced.Faces.Count;
                        reduced = SurfaceVisibilityFilter.Filter(reduced, project, scene, maps, cancellation, context.Work, update => context.ReportOperation(Name, update), compute);
                        rejectedAfterCompletion = facesBeforeVisibility - reduced.Faces.Count;
                        context.ReportProgress(Name, $"Rejected {rejectedAfterCompletion:N0} completed triangles contradicted by observed empty space.");
                    }
                    context.ReportProgress(Name, "Repairing small enclosed holes after visibility cleanup…");
                    context.BeginOperation(Name, "Final hole repair");
                    var finalRepair = meshBuilder.RepairFinalHoles(project, scene, maps, reduced, volume.VoxelSize, cancellation, context.Work, update => context.ReportOperation(Name, update));
                    context.ReportProgress(Name, $"Closed {finalRepair.RepairedHoles:N0} small holes with {finalRepair.AddedFaces:N0} triangles.");
                    if (reduced.Faces.Count > project.Settings.FaceLimit)
                    {
                        reduced = edgeCollapse.Reduce(reduced, project.Settings.FaceLimit, cancellation, work: context.Work, progress: message => context.ReportProgress(Name, message));
                    }
                    var vegetationRemovedFaces = 0;
                    if (project.Settings.OptimiseVegetation)
                    {
                        context.ReportProgress(Name, "Allocating fewer triangles to vegetation confirmed by multiple visible photographs…");
                        context.BeginOperation(Name, "Vegetation classification");
                        var labels = SurfaceVegetationClassifier.Classify(reduced, project, scene, maps, semantics, cancellation, context.Work, update => context.ReportOperation(Name, update), compute);
                        var tolerances = SurfaceVegetationClassifier.Tolerances(reduced, labels,
                            detailTolerance * project.Settings.VegetationMeshToleranceMultiplier, context.Work, cancellation);
                        var before = reduced.Faces.Count;
                        context.BeginOperation(Name, "Vegetation simplification");
                        reduced = edgeCollapse.Reduce(reduced, before, cancellation, vertexTolerances: tolerances, work: context.Work, progress: message => context.ReportProgress(Name, message));
                        vegetationRemovedFaces = before - reduced.Faces.Count;
                        context.ReportProgress(Name, $"Removed {vegetationRemovedFaces:N0} vegetation triangles; rock and material boundaries remain fixed.");
                    }
                    if (project.Settings.MeshIslandMode != IslandTrimMode.Off)
                    {
                        context.ReportProgress(Name, "Trimming disconnected islands after surface completion…");
                    }
                    context.BeginOperation(Name, "Island trimming");
                    var trimming = SurfaceIslandTrimmer.Trim(reduced, project.Settings.MeshIslandMode,
                        project.Settings.MeshIslandMinimumAreaPercent, cancellation, context.Work, update => context.ReportOperation(Name, update));
                    if (trimming.InputComponents.HasValue)
                    {
                        context.ReportProgress(Name, $"Removed {trimming.RemovedComponents:N0} disconnected islands ({trimming.RemovedFaces:N0} triangles).");
                    }
                    // Removed fragments must not continue to occlude photographs
                    // of the surviving surface during source assignment.
                    context.BeginOperation(Name, "Photograph selection");
                    var mesh = meshBuilder.AssignCameras(project, scene, maps, trimming.Surface, cancellation,
                        message => context.ReportProgress(Name, message), retainUnobserved: true);
                    var photographCoverage = SurfacePhotographCoverage.Measure(mesh, cancellation);
                    context.ReportProgress(Name, $"Photographs assigned to {photographCoverage.Faces - photographCoverage.InferredFaces:N0}/{photographCoverage.Faces:N0} triangles. Inferred colour covers {photographCoverage.InferredAreaPercent:F2}% of total surface area, including hidden and completed surfaces.");
                    if (project.Settings.OptimiseVegetation)
                    {
                        var labels = SurfaceVegetationClassifier.Classify(mesh, project, scene, maps, semantics, cancellation, context.Work, update => context.ReportOperation(Name, update), compute);
                        var faces = mesh.Faces.Select(face => face.All(vertex => labels[vertex])).ToArray();
                        fileOperations.AtomicJson(Path.Combine(dir, "vegetation.json"), faces);
                    }
                    context.BeginOperation(Name, "Saving mesh");
                    fileOperations.AtomicData(Path.Combine(dir, "mesh.json"), mesh, cancellation);
                    plyWriter.Write(Path.Combine(dir, "mesh.ply"), mesh.Vertices, mesh.Faces);
                    fileOperations.AtomicJson(Path.Combine(dir, "mesh.report.json"), new
                    {
                        vegetationRemovedFaces,
                        extractedFaces = extracted.Faces.Count,
                        reducedFaces = reduced.Faces.Count,
                        texturedFaces = mesh.Faces.Count,
                        inferredColourFaces = mesh.SourceImages.Count(string.IsNullOrEmpty),
                        photographCoverage,
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
