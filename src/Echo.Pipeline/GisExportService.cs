using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Export;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Texturing;

namespace Echo.Pipeline;

[Export(typeof(IGisExportService)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class GisExportService : IGisExportService
{
    private readonly IJsonCodec json;
    private readonly IGisWriter writer;

    [ImportingConstructor]
    public GisExportService(IJsonCodec json, IGisWriter writer)
    {
        this.json = json;
        this.writer = writer;
    }

    public GisExportResult Export(IProjectStore store, string runId, string destination)
    {
        var run = store.GetRun(runId);
        if (!run.Artifacts.TryGetValue("alignment", out var alignment))
        {
            throw new EchoException("A completed alignment is required for geographic export.", 2);
        }
        var scene = json.Read<ReconstructionOverview>(store.PathFor(alignment + "/scene.json")).ToScene();
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new EchoException("Choose a new export folder; existing files are never overwritten.", 2);
        }
        foreach (var protectedFolder in new[] { "artifacts", "sources", "runs", "work" })
        {
            var protectedPath = Path.GetFullPath(store.PathFor(protectedFolder)) + Path.DirectorySeparatorChar;
            if ((destination + Path.DirectorySeparatorChar).StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new EchoException("Export outside the project's managed artifact, source, run and work folders.", 2);
            }
        }
        var surface = ReadSurface(store, run);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            writer.Write(staging, scene, surface, run.Snapshot.Images);
            if (run.Artifacts.TryGetValue("export", out var export))
            {
                foreach (var file in new[] { "cliff.glb", "cliff.geo.json" })
                {
                    var source = store.PathFor(export + "/" + file);
                    if (File.Exists(source))
                    {
                        File.Copy(source, Path.Combine(staging, file));
                    }
                }
            }
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        return new GisExportResult
        {
            Directory = destination,
            RunId = run.Id,
            Cameras = scene.Cameras.Count,
            Triangles = surface?.Faces.Count ?? 0
        };
    }

    private Surface? ReadSurface(IProjectStore store, RunData run)
    {
        if (!run.Artifacts.TryGetValue("mesh", out var mesh))
        {
            return null;
        }
        var file = store.PathFor(mesh + "/mesh.json");
        if (run.Artifacts.TryGetValue("texture", out var texture))
        {
            var result = json.Read<TextureResult>(store.PathFor(texture + "/texture.json"));
            if (result.MeshFile != null)
            {
                file = store.PathFor(texture + "/" + result.MeshFile);
            }
        }
        return json.Read<Surface>(file);
    }
}
