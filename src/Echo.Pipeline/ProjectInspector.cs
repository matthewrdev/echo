using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Pipeline;

public sealed class ProjectInspector : IProjectInspector
{
    private readonly IJsonCodec jsonCodec;
    private readonly IAlignmentPreviewReader previewReader;

    public ProjectInspector(IJsonCodec jsonCodec, IAlignmentPreviewReader previewReader)
    {
        this.jsonCodec = jsonCodec;
        this.previewReader = previewReader;
    }

    public Inspection Inspect(IProjectStore store, string runId = "latest", bool summary = false)
    {
        var project = store.Load();
        var warnings = new List<string>();
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
                var preview = store.PathFor(alignment + "/preview.json");
                var binaryPreview = store.PathFor(alignment + "/preview.bin");
                var file = store.PathFor(alignment + "/scene.json");
                scene = ReadArtifact(() => File.Exists(binaryPreview) ? previewReader.Read(binaryPreview, summary).Scene
                    : File.Exists(preview) ? ReadPreview(preview, summary) : summary
                        ? jsonCodec.Read<ReconstructionOverview>(file).ToScene()
                        : jsonCodec.Read<ReconstructionScene>(file),
                    "alignment", summary, warnings);
                if (key == "alignment" && scene != null)
                {
                    var enabled = run.Snapshot.Images.Where(photo => photo.Enabled).Select(photo => photo.Id).ToHashSet();
                    var registered = scene.Cameras.Select(camera => camera.ImageId).Distinct().Count(enabled.Contains);
                    if (enabled.Count > 0 && registered < enabled.Count / 2.0)
                    {
                        warnings.Add($"Weak alignment: {registered:N0} of {enabled.Count:N0} enabled photos registered. Depth and Cloud cover only the registered views.");
                    }
                }
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
                cloud = ReadArtifact(() => summary
                        ? jsonCodec.Read<DenseCloudOverview>(file).ToReport()
                        : jsonCodec.Read<DenseCloudReport>(file),
                    "cloud", summary, warnings);
            }
        }

        return new Inspection
        {
            SchemaVersion = 1,
            ProjectRoot = store.Root,
            Project = project,
            Run = run,
            Reconstruction = scene,
            Surface = surface,
            Cloud = cloud,
            Warnings = warnings
        };
    }

    private ReconstructionScene ReadPreview(string file, bool summary)
    {
        var scene = jsonCodec.Read<AlignmentPreview>(file).Scene;
        if (summary)
        {
            scene.Points = [];
        }
        return scene;
    }

    private T? ReadArtifact<T>(Func<T> read, string stage, bool summary, List<string> warnings) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception error) when (summary && error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            warnings.Add($"Saved {stage} could not be read: {error.Message}");
            return null;
        }
    }
}
