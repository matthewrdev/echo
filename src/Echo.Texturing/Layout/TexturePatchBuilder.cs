using Echo.Compute;
using Echo.Models;
using Echo.Storage.Resources;
using Echo.Storage;
using Echo.Telemetry;
using Echo.Texturing.Charts;

namespace Echo.Texturing.Layout;

public sealed class TexturePatchBuilder : ITexturePatchBuilder
{
    private readonly ITextureChartOrienter orienter;
    private readonly IComputeService? compute;
    private readonly IPerformanceTrace? trace;

    public TexturePatchBuilder(ITextureChartOrienter orienter, IComputeService? compute = null,
        IPerformanceTrace? trace = null)
    {
        this.orienter = orienter;
        this.compute = compute;
        this.trace = trace;
    }

    public IReadOnlyList<TexturePatch> Build(ProjectData project, ReconstructionScene scene, Surface mesh,
        CancellationToken cancellation, IReadOnlyList<bool>? vegetationFaces = null, Action<string>? progress = null, WorkBuffers? work = null, IReadOnlyList<int>? priorityBands = null, Action<OperationProgress>? operationProgress = null)
    {
        if (vegetationFaces != null && vegetationFaces.Count != mesh.Faces.Count)
        {
            throw new ArgumentException("Expected one classification per surface face.", nameof(vegetationFaces));
        }

        if (project.Settings.TextureChartFaces < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(project), "A chart must allow at least one triangle.");
        }

        if (priorityBands != null && priorityBands.Count != mesh.Faces.Count)
        {
            throw new ArgumentException("Expected one priority band per surface face.", nameof(priorityBands));
        }

        string[] steps = ["Chart geometry", "Partition regions", "Parameterize charts", "Merge charts", "Orient charts"];
        operationProgress?.Invoke(new OperationProgress("UV charts", "Building texture chart geometry…") { Steps = steps, StepIndex = 0 });
        var groups = vegetationFaces == null && priorityBands == null ? null
            : Enumerable.Range(0, mesh.Faces.Count).Select(face =>
                (priorityBands?[face] ?? 0) * 2 + (vegetationFaces?[face] == true ? 1 : 0)).ToArray();
        TextureChartGeometry geometry;
        using (var building = trace?.Begin("Build texture chart geometry", PerformanceSpanKind.Operation))
        {
            geometry = new TextureChartGeometry(mesh, cancellation, work);
            building?.Measure(mesh.Faces.Count);
        }

        List<List<int>> regions;
        operationProgress?.Invoke(new OperationProgress("UV charts", "Partitioning connected chart regions…") { Steps = steps, StepIndex = 1 });
        using (trace?.Begin("Partition texture chart regions", PerformanceSpanKind.Operation))
        {
            regions = TextureChartRegions.Build(geometry, project.Settings.TextureChartFaces,
                project.Settings.TextureChartAngle, groups, cancellation);
        }

        var patches = new List<TexturePatch>();
        var scratch = new TextureLinearSystem(0);
        var compactAtlas = project.Settings.TextureSize <= 1024;
        progress?.Invoke($"Parameterizing {regions.Count:N0} connected surface regions…");
        var completed = 0;
        operationProgress?.Invoke(new OperationProgress("UV charts", "Parameterizing connected surface regions…", 0, regions.Count, "regions") { Steps = steps, StepIndex = 2 });
        using (var unwrapping = trace?.Begin("Parameterize texture charts", PerformanceSpanKind.Operation, PerformanceBackend.Mixed))
        {
            foreach (var region in regions)
            {
                Unwrap(geometry, region, patches, cancellation, project.Settings.ComputeBackend, scratch, compactAtlas);
                completed++;
                if (completed % 1000 == 0 || completed == regions.Count)
                {
                    progress?.Invoke($"Parameterizing surface regions: {completed:N0}/{regions.Count:N0}…");
                    operationProgress?.Invoke(new OperationProgress("UV charts", "Parameterizing connected surface regions…", completed, regions.Count, "regions") { Steps = steps, StepIndex = 2 });
                }
            }

            unwrapping?.Measure(regions.Count);
        }

        operationProgress?.Invoke(new OperationProgress("UV charts", "Merging compatible UV charts…") { Steps = steps, StepIndex = 3 });
        using (trace?.Begin("Merge texture charts", PerformanceSpanKind.Operation, PerformanceBackend.Mixed))
        {
            patches = TextureChartMerger.Merge(geometry, patches, project.Settings.TextureChartFaces, groups, cancellation,
                progress, compute, project.Settings.ComputeBackend, trace, compactAtlas);
        }

        operationProgress?.Invoke(new OperationProgress("UV charts", "Orienting UV charts…") { Steps = steps, StepIndex = 4 });
        using (trace?.Begin("Orient texture charts", PerformanceSpanKind.Operation))
        {
            foreach (var patch in patches)
            {
                orienter.Orient(patch);
            }
        }

        trace?.Measurement("Texture charts per layout", patches.Count, "charts");
        return patches.OrderByDescending(patch => patch.Bounds.Height).ToArray();
    }

    private void Unwrap(TextureChartGeometry geometry, List<int> faces, List<TexturePatch> patches,
        CancellationToken cancellation, ComputeBackend backend, TextureLinearSystem scratch, bool compactAtlas)
    {
        cancellation.ThrowIfCancellationRequested();
        var chart = new TextureChartMesh(geometry, faces);
        var conformal = TextureConformalMap.Solve(chart, cancellation, compute, backend, scratch);
        var coordinates = conformal;
        var accepted = coordinates != null && TextureChartValidation.Accept(chart, coordinates, cancellation, compactAtlas);
        if (!accepted)
        {
            coordinates = TexturePlanarMap.Solve(chart, cancellation);
            accepted = coordinates != null && TextureChartValidation.Accept(chart, coordinates, cancellation, compactAtlas);
        }

        if (!accepted)
        {
            coordinates = TextureHarmonicMap.Solve(chart, cancellation, compute, backend);
            accepted = coordinates != null && TextureChartValidation.Accept(chart, coordinates, cancellation, compactAtlas);
        }

        if (accepted)
        {
            patches.Add(TextureChartPatch.Create(geometry, chart, faces, coordinates!));
            return;
        }

        if (faces.Count == 1)
        {
            throw new EchoException("Cannot unwrap a degenerate surface triangle.", 5);
        }

        var parts = TextureChartRegions.SplitLocalFold(geometry, chart, faces, conformal, cancellation)
            ?? TextureChartRegions.Split(geometry, faces, cancellation);
        foreach (var part in parts)
        {
            Unwrap(geometry, part, patches, cancellation, backend, scratch, compactAtlas);
        }
    }
}
