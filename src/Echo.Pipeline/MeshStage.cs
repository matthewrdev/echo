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
            completionVersion = 10,
            project.Settings.ExclusionRegions,
            simplificationVersion = 5,
            project.Settings.ComputeBackend,
            computeImplementation = context.ComputeImplementation(ComputeOperation.Mesh),
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
                    var progress = new MeshProgress(project.Settings, update => context.ReportOperation(Name, update));
                    progress.Begin(MeshPhase.LoadInputs);
                    context.ReportProgress(Name, $"Processing RAM budget: {context.Resources.MemoryBytes / 1073741824.0:G4} GiB; shared page cache {context.Resources.CacheBytes / 1048576:N0} MiB. Storage mode: {context.Resources.Mode}.");
                    var semantics = semanticUnits.ToDictionary(pair => pair.Key,
                        pair => context.ReadData<SemanticMap>(store.PathFor(pair.Value + "/semantics.json")));
                    var maps = context.DepthMaps();
                    var depthContext = new MeshDepthContext(project, scene, maps);
                    var volume = context.ReadData<FusedVolume>(store.PathFor(fusionPath + "/volume.json"));
                    progress.Begin(MeshPhase.ExtractSurface);
                    using var geometryBuffers = new MeshGeometryBuffers(context.Work);
                    var extracted = geometryBuffers.Replace(work => meshBuilder.Extract(volume, cancellation, work,
                        message => context.ReportProgress(Name, message), progress.Report, project.Settings.ComputeBackend));
                    extracted = geometryBuffers.Replace(work => SurfaceExclusionFilter.Filter(extracted, project.Settings.ExclusionRegions, cancellation, work));
                    var extractedFaces = extracted.Faces.Count;
                    var detailTolerance = volume.VoxelSize * .15;
                    progress.Begin(MeshPhase.SimplifyGeometry);
                    var reduced = geometryBuffers.Replace(work => edgeCollapse.Reduce(extracted, project.Settings.FaceLimit, cancellation, detailTolerance, work: work, progress: progress.Report));
                    progress.Begin(MeshPhase.RepairObservedHoles);
                    meshBuilder.FillHoles(project, scene, maps, reduced, volume.VoxelSize, cancellation, context.Work, progress.Report, depthContext);
                    reduced = geometryBuffers.Replace(work => SurfaceExclusionFilter.Filter(reduced, project.Settings.ExclusionRegions, cancellation, work));
                    if (reduced.Faces.Count > project.Settings.FaceLimit)
                    {
                        reduced = geometryBuffers.Replace(work => edgeCollapse.Reduce(reduced, project.Settings.FaceLimit, cancellation, work: work, progress: progress.Report));
                    }
                    progress.Begin(MeshPhase.CompleteSurface);
                    context.ReportProgress(Name, "Removing surfaces contradicted by visible depth rays before completion…");
                    var facesBeforeVisibility = reduced.Faces.Count;
                    reduced = geometryBuffers.Replace(work => SurfaceVisibilityFilter.Filter(reduced, project, scene, maps, cancellation, work, progress.Report, compute, depthContext));
                    var rejectedBeforeCompletion = facesBeforeVisibility - reduced.Faces.Count;
                    var rejectedAfterCompletion = 0;
                    context.ReportProgress(Name, $"Rejected {rejectedBeforeCompletion:N0} triangles contradicted by observed empty space.");
                    progress.Report(new OperationProgress("Find surface gaps", "Checking the surface for gaps…"));
                    var completed = SurfaceSupportFilter.HasGaps(reduced, cancellation, context.Work);
                    if (completed)
                    {
                        progress.Report(new OperationProgress("Completion photograph selection", "Selecting photographs for surface completion…"));
                        using var observedWork = context.Work?.CreateScope();
                        var observed = meshBuilder.AssignCameras(project, scene, maps, reduced, cancellation,
                            operationProgress: progress.Report, work: observedWork, forCompletion: true, depthContext: depthContext);
                        using var completionBuffers = new MeshGeometryBuffers(context.Work);
                        progress.Report(new OperationProgress("Surface reconstruction", "Reconstructing missing surface…"));
                        var reconstructed = completionBuffers.Replace(work => surfaceReconstructor.Reconstruct(observed, scene, volume.VoxelSize, cancellation,
                            message => context.ReportProgress(Name, message), context.Memory, work));
                        reconstructed = completionBuffers.Replace(work => SurfaceExclusionFilter.Filter(reconstructed, project.Settings.ExclusionRegions, cancellation, work));
                        context.ReportProgress(Name, "Restricting surface completion to the scanned neighbourhood…");
                        var supported = completionBuffers.Replace(work => SurfaceSupportFilter.Filter(reconstructed, observed, volume.VoxelSize * 24, cancellation, work, progress.Report));
                        context.ReportProgress(Name, "Adapting the completed surface to its geometric detail…");
                        reduced = geometryBuffers.Replace(work => edgeCollapse.Reduce(supported, project.Settings.FaceLimit, cancellation, detailTolerance, work: work, progress: progress.Report));
                        context.ReportProgress(Name, "Checking completed surfaces against observed empty space…");
                        facesBeforeVisibility = reduced.Faces.Count;
                        reduced = geometryBuffers.Replace(work => SurfaceVisibilityFilter.Filter(reduced, project, scene, maps, cancellation, work, progress.Report, compute, depthContext));
                        rejectedAfterCompletion = facesBeforeVisibility - reduced.Faces.Count;
                        context.ReportProgress(Name, $"Rejected {rejectedAfterCompletion:N0} completed triangles contradicted by observed empty space.");
                    }
                    context.ReportProgress(Name, "Repairing small enclosed holes after visibility cleanup…");
                    progress.Begin(MeshPhase.RepairFinalHoles);
                    var finalRepair = meshBuilder.RepairFinalHoles(project, scene, maps, reduced, volume.VoxelSize, cancellation, context.Work, progress.Report, depthContext);
                    context.ReportProgress(Name, $"Closed {finalRepair.RepairedHoles:N0} small holes with {finalRepair.AddedFaces:N0} triangles.");
                    if (reduced.Faces.Count > project.Settings.FaceLimit)
                    {
                        reduced = geometryBuffers.Replace(work => edgeCollapse.Reduce(reduced, project.Settings.FaceLimit, cancellation, work: work, progress: progress.Report));
                    }
                    var vegetationRemovedFaces = 0;
                    if (project.Settings.OptimiseVegetation)
                    {
                        context.ReportProgress(Name, "Allocating fewer triangles to vegetation confirmed by multiple visible photographs…");
                        progress.Begin(MeshPhase.OptimiseVegetation);
                        using var vegetationWork = context.Work?.CreateScope();
                        var labels = SurfaceVegetationClassifier.Classify(reduced, project, scene, maps, semantics, cancellation, vegetationWork, progress.Report, compute, depthContext);
                        var tolerances = SurfaceVegetationClassifier.Tolerances(reduced, labels,
                            detailTolerance * project.Settings.VegetationMeshToleranceMultiplier, vegetationWork, cancellation);
                        var before = reduced.Faces.Count;
                        progress.Report(new OperationProgress("Vegetation simplification", "Simplifying vegetation…"));
                        reduced = geometryBuffers.Replace(work => edgeCollapse.Reduce(reduced, before, cancellation, vertexTolerances: tolerances, work: work, progress: progress.Report));
                        vegetationRemovedFaces = before - reduced.Faces.Count;
                        context.ReportProgress(Name, $"Removed {vegetationRemovedFaces:N0} vegetation triangles; rock and material boundaries remain fixed.");
                    }
                    var facesBeforeExclusions = reduced.Faces.Count;
                    reduced = geometryBuffers.Replace(work => SurfaceExclusionFilter.Filter(reduced, project.Settings.ExclusionRegions, cancellation, work));
                    var excludedFaces = facesBeforeExclusions - reduced.Faces.Count;
                    if (project.Settings.ExclusionRegions.Any(region => region.Enabled))
                    {
                        context.ReportProgress(Name, $"Removed {excludedFaces:N0} triangles outside the crop selection after surface completion.");
                    }
                    if (project.Settings.MeshIslandMode != IslandTrimMode.Off)
                    {
                        progress.Begin(MeshPhase.TrimIslands);
                        context.ReportProgress(Name, "Trimming disconnected islands after surface completion…");
                    }
                    var trimming = SurfaceIslandTrimmer.Trim(reduced, project.Settings.MeshIslandMode,
                        project.Settings.MeshIslandMinimumAreaPercent, cancellation, context.Work, progress.Report);
                    if (trimming.InputComponents.HasValue)
                    {
                        context.ReportProgress(Name, $"Removed {trimming.RemovedComponents:N0} disconnected islands ({trimming.RemovedFaces:N0} triangles).");
                    }
                    // Removed fragments must not continue to occlude photographs
                    // of the surviving surface during source assignment.
                    progress.Begin(MeshPhase.SelectPhotographs);
                    var mesh = meshBuilder.AssignCameras(project, scene, maps, trimming.Surface, cancellation,
                        retainUnobserved: true, operationProgress: progress.Report, work: context.Work, depthContext: depthContext);
                    var photographCoverage = SurfacePhotographCoverage.Measure(mesh, cancellation);
                    context.ReportProgress(Name, $"Photographs assigned to {photographCoverage.Faces - photographCoverage.InferredFaces:N0}/{photographCoverage.Faces:N0} triangles. Inferred colour covers {photographCoverage.InferredAreaPercent:F2}% of total surface area, including hidden and completed surfaces.");
                    if (project.Settings.OptimiseVegetation)
                    {
                        using var vegetationWork = context.Work?.CreateScope();
                        var labels = SurfaceVegetationClassifier.Classify(mesh, project, scene, maps, semantics, cancellation, vegetationWork, progress.Report, compute, depthContext);
                        var faces = SurfaceVegetationClassifier.FaceLabels(mesh, labels, cancellation);
                        fileOperations.AtomicJson(Path.Combine(dir, "vegetation.json"), faces);
                    }
                    progress.Begin(MeshPhase.SaveMesh);
                    fileOperations.AtomicData(Path.Combine(dir, "mesh.json"), mesh, cancellation);
                    plyWriter.Write(Path.Combine(dir, "mesh.ply"), mesh.Vertices, mesh.Faces);
                    fileOperations.AtomicJson(Path.Combine(dir, "mesh.report.json"), new
                    {
                        vegetationRemovedFaces,
                        excludedFaces,
                        extractedFaces,
                        reducedFaces = reduced.Faces.Count,
                        texturedFaces = mesh.Faces.Count,
                        inferredColourFaces = photographCoverage.InferredFaces,
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
