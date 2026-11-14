using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Pipeline;

[Export(typeof(IProjectInspector)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ProjectInspector : IProjectInspector
{
    private readonly IJsonCodec jsonCodec;

    [ImportingConstructor]
    public ProjectInspector(IJsonCodec jsonCodec)
    {
        this.jsonCodec = jsonCodec;
    }

    public Inspection Inspect(IProjectStore store, string runId = "latest", bool summary = false)
    {
        RunData? run = null;
        ReconstructionScene? scene = null;
        Surface? surface = null;
        DenseCloudReport? cloud = null;
        try
        {
            run = store.GetRun(runId);
        }
        catch (EchoException) when (runId == "latest")
        {
        }

        if (run != null)
        {
            var key = run.Artifacts.ContainsKey("alignment") ? "alignment" : "alignmentPreview";
            if (run.Artifacts.TryGetValue(key, out var alignment))
            {
                var file = store.PathFor(alignment + "/scene.json");
                scene = summary
                    ? jsonCodec.Read<ReconstructionOverview>(file).ToScene()
                    : jsonCodec.Read<ReconstructionScene>(file);
            }

            if (!summary && run.Artifacts.TryGetValue("mesh", out var mesh))
            {
                var file = store.PathFor(mesh + "/mesh.json");
                // Full-resolution meshes are opened explicitly by the artifact viewer,
                // rather than serialised into every progress poll.
                if (new FileInfo(file).Length < 10000000)
                {
                    surface = jsonCodec.Read<Surface>(file);
                }
            }

            if (run.Artifacts.TryGetValue("cloud", out var dense))
            {
                var file = store.PathFor(dense + "/cloud.json");
                cloud = summary
                    ? jsonCodec.Read<DenseCloudOverview>(file).ToReport()
                    : jsonCodec.Read<DenseCloudReport>(file);
            }
        }

        return new Inspection
        {
            SchemaVersion = 1,
            ProjectRoot = store.Root,
            Project = store.Load(),
            Run = run,
            Reconstruction = scene,
            Surface = surface,
            Cloud = cloud
        };
    }
}
