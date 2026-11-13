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

    public Inspection Inspect(IProjectStore store, string runId = "latest")
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
                scene = jsonCodec.Read<ReconstructionScene>(store.PathFor(alignment + "/scene.json"));
            }

            if (run.Artifacts.TryGetValue("mesh", out var mesh))
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
                cloud = jsonCodec.Read<DenseCloudReport>(store.PathFor(dense + "/cloud.json"));
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
