using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Compute;
using Echo.Storage;

namespace Echo.Texturing;

[Export(typeof(ITexturePatchBuilder)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TexturePatchBuilder : ITexturePatchBuilder
{
    private readonly ITextureChartOrienter orienter;
    private readonly IComputeService? compute;

    [ImportingConstructor]
    public TexturePatchBuilder(ITextureChartOrienter orienter, [Import(AllowDefault = true)] IComputeService? compute = null)
    {
        this.orienter = orienter;
        this.compute = compute;
    }

    public IReadOnlyList<TexturePatch> Build(ProjectData project, ReconstructionScene scene, Surface mesh,
        CancellationToken cancellation, IReadOnlyList<bool>? vegetationFaces = null, Action<string>? progress = null, WorkBuffers? work = null)
    {
        if (vegetationFaces != null && vegetationFaces.Count != mesh.Faces.Count)
        {
            throw new ArgumentException("Expected one classification per surface face.", nameof(vegetationFaces));
        }
        if (project.Settings.TextureChartFaces < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(project), "A chart must allow at least one triangle.");
        }
        var geometry = new TextureChartGeometry(mesh, cancellation, work);
        var regions = TextureChartRegions.Build(geometry, project.Settings.TextureChartFaces,
            project.Settings.TextureChartAngle, vegetationFaces, cancellation);
        var patches = new List<TexturePatch>();
        progress?.Invoke($"Parameterizing {regions.Count:N0} connected surface regions…");
        var completed = 0;
        foreach (var region in regions)
        {
            Unwrap(geometry, region, patches, cancellation, project.Settings.ComputeBackend);
            completed++;
            if (completed % 1000 == 0)
            {
                progress?.Invoke($"Parameterizing surface regions: {completed:N0}/{regions.Count:N0}…");
            }
        }

        patches = TextureChartMerger.Merge(geometry, patches, project.Settings.TextureChartFaces, vegetationFaces, cancellation, progress, compute, project.Settings.ComputeBackend);
        foreach (var patch in patches)
        {
            orienter.Orient(patch);
        }
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
