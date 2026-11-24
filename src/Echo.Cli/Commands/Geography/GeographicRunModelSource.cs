using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Texturing;

namespace Echo.Cli.Commands.Geography;

internal sealed class GeographicRunModelSource : IGeographicRunModelSource
{
    private readonly IJsonCodec json;

    public GeographicRunModelSource(IJsonCodec json)
    {
        this.json = json;
    }

    public GeographicModelSource? Resolve(IProjectStore store, RunData run, GeoPosition origin)
    {
        if (run.Artifacts.TryGetValue("export", out var export))
        {
            return new GeographicModelSource(store.PathFor(export + "/cliff.glb"), origin);
        }

        if (!run.Artifacts.TryGetValue("mesh", out var mesh))
        {
            return null;
        }

        var surface = store.PathFor(mesh + "/mesh.json");
        if (!run.Artifacts.TryGetValue("texture", out var texture))
        {
            return new GeographicModelSource(surface, origin);
        }

        var reportPath = store.PathFor(texture + "/texture.json");
        var report = json.Read<TextureResult>(reportPath);
        if (report.MeshFile != null)
        {
            surface = ArtifactFile(store, texture, report.MeshFile);
        }

        var coordinates = ArtifactFile(store, texture, report.UvsFile);
        return new GeographicModelSource(surface, origin, File.Exists(coordinates) ? coordinates : reportPath,
            store.PathFor(texture + "/atlas.jpg"));
    }

    private string ArtifactFile(IProjectStore store, string folder, string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || Path.GetFileName(name) != name)
        {
            throw new EchoException("Invalid texture artifact file name.", 4);
        }

        return store.PathFor(folder + "/" + name);
    }
}
