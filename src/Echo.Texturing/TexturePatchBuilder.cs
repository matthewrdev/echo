using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.Storage;
using Echo.Telemetry;

namespace Echo.Texturing;

[Export(typeof(ITexturePatchBuilder)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TexturePatchBuilder : ITexturePatchBuilder
{
    private readonly ITextureChartOrienter orienter;
    private readonly IComputeService? compute;
    private readonly IPerformanceTrace? trace;

    [ImportingConstructor]
    public TexturePatchBuilder(ITextureChartOrienter orienter, [Import(AllowDefault = true)] IComputeService? compute = null,
        [Import(AllowDefault = true)] IPerformanceTrace? trace = null)
    {
        this.orienter = orienter;
        this.compute = compute;
        this.trace = trace;
    }

    public IReadOnlyList<TexturePatch> Build(ProjectData project, ReconstructionScene scene, Surface mesh,
        CancellationToken cancellation, IReadOnlyList<bool>? vegetationFaces = null, Action<string>? progress = null, WorkBuffers? work = null, IReadOnlyList<int>? priorityBands = null)
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
        using (trace?.Begin("Partition texture chart regions", PerformanceSpanKind.Operation))
        {
            regions = TextureChartRegions.Build(geometry, project.Settings.TextureChartFaces,
                project.Settings.TextureChartAngle, groups, cancellation);
        }
        var patches = new List<TexturePatch>();
        progress?.Invoke($"Parameterizing {regions.Count:N0} connected surface regions…");
        var completed = 0;
        using (var unwrapping = trace?.Begin("Parameterize texture charts", PerformanceSpanKind.Operation, PerformanceBackend.Mixed))
        {
            foreach (var region in regions)
            {
                Unwrap(geometry, region, patches, cancellation, project.Settings.ComputeBackend);
                completed++;
                if (completed % 1000 == 0)
                {
                    progress?.Invoke($"Parameterizing surface regions: {completed:N0}/{regions.Count:N0}…");
                }
            }
            unwrapping?.Measure(regions.Count);
        }

        using (trace?.Begin("Merge texture charts", PerformanceSpanKind.Operation, PerformanceBackend.Mixed))
        {
            patches = TextureChartMerger.Merge(geometry, patches, project.Settings.TextureChartFaces, groups, cancellation, progress, compute, project.Settings.ComputeBackend);
        }
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
        CancellationToken cancellation, ComputeBackend backend)
    {
        cancellation.ThrowIfCancellationRequested();
        var chart = new TextureChartMesh(geometry, faces);
        var coordinates = TextureConformalMap.Solve(chart, cancellation, compute, backend);
        if (coordinates == null || !TextureChartValidation.Accept(chart, coordinates, cancellation))
        {
            coordinates = TextureHarmonicMap.Solve(chart, cancellation, compute, backend);
            if (coordinates == null || !TextureChartValidation.Accept(chart, coordinates, cancellation))
            {
                if (faces.Count == 1)
                {
                    throw new EchoException("Cannot unwrap a degenerate surface triangle.", 5);
                }

                foreach (var part in TextureChartRegions.Split(geometry, faces, cancellation))
                {
                    Unwrap(geometry, part, patches, cancellation, backend);
                }
                return;
            }
        }

        patches.Add(TextureChartPatch.Create(geometry, chart, faces, coordinates));
    }
}
