using System.Text.Json;
using Echo.Export;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Alignment;
using Echo.Projects;
using Echo.Storage.Artifacts;
using Echo.Storage.Journals;
using Echo.Storage.Matching;
using Echo.Texturing;

namespace Echo.Pipeline.Previews;

public sealed class StagePreviewArtifacts : IStagePreviewArtifacts
{
    private readonly IJsonCodec json;
    private readonly IFeatureArtifactStore features;
    private readonly IMatchArtifactStore matches;
    private readonly IMatchingIndexStore indexes;
    private readonly IAlignmentPreviewReader alignment;
    private readonly IBulkArtifactStore bulk;
    private readonly IGlbWriter glb;

    public StagePreviewArtifacts(IJsonCodec json, IFeatureArtifactStore features, IMatchArtifactStore matches,
        IMatchingIndexStore indexes, IAlignmentPreviewReader alignment, IBulkArtifactStore bulk, IGlbWriter glb)
    {
        this.json = json;
        this.features = features;
        this.matches = matches;
        this.indexes = indexes;
        this.alignment = alignment;
        this.bulk = bulk;
        this.glb = glb;
    }

    public FeatureSet Features(IProjectStore store, RunData run, string imageId)
    {
        var path = ImageArtifact(store, run, "features", imageId, "features.json");
        return features.Read(path, descriptors: false);
    }

    public ImagePair Matching(IProjectStore store, RunData run, string firstId, string secondId)
    {
        var entry = MatchEntries(store, run).FirstOrDefault(pair =>
            IsPair(pair.A, pair.B, firstId, secondId));
        if (entry == null)
        {
            throw new EchoException($"Run {run.Id} has no saved match pair for {firstId} and {secondId}.", 2);
        }

        return matches.Read(store.PathFor(entry.Artifact + "/pair.json"));
    }

    private IEnumerable<MatchingIndexEntry> MatchEntries(IProjectStore store, RunData run)
    {
        var folder = StageFolder(run, "matching", allowPreview: true);
        var path = store.PathFor(folder + "/pairs.bin");
        if (File.Exists(path) || !run.Artifacts.ContainsKey("matching"))
        {
            foreach (var entry in indexes.Read(path))
            {
                yield return entry;
            }

            yield break;
        }

        // Older completed runs may only have the unit list.
        var units = json.Read<List<string>>(store.PathFor(folder + "/units.json"));
        for (var index = 0; index < units.Count; index++)
        {
            yield return indexes.Describe(store.Root, units[index], index);
        }
    }

    public ReconstructionScene Alignment(IProjectStore store, RunData run)
    {
        var folder = StageFolder(run, "alignment", allowPreview: true);
        if (run.Artifacts.ContainsKey("alignment"))
        {
            using var session = bulk.OpenSession(64L * 1024 * 1024, 0);
            var scene = session.Read<ReconstructionScene>(store.PathFor(folder + "/scene.json"));
            return AlignmentPreviewSampler.Sample(scene).Scene;
        }

        return alignment.Read(store.PathFor(folder + "/preview.bin"), summary: false).Scene;
    }

    public string DepthPath(IProjectStore store, RunData run, string imageId)
    {
        return ImageArtifact(store, run, "depth", imageId, "depth.json");
    }

    public DenseCloudReport Cloud(IProjectStore store, RunData run, bool includePoints = true)
    {
        var folder = StageFolder(run, "cloud", allowPreview: true);
        return new DenseCloudReportCodec().Read(store.PathFor(folder + "/cloud.bin"), includePreview: includePoints);
    }

    public string FusionPath(IProjectStore store, RunData run)
    {
        return store.PathFor(StageFolder(run, "fusion") + "/volume.json");
    }

    public string MeshPath(IProjectStore store, RunData run)
    {
        return store.PathFor(StageFolder(run, "mesh") + "/mesh.ply");
    }

    public void WriteTextureModel(IProjectStore store, RunData run, string output)
    {
        var folder = StageFolder(run, "texture");
        var texture = json.Read<TextureResult>(store.PathFor(folder + "/texture.json"));
        using var session = bulk.OpenSession(64L * 1024 * 1024, 0);
        var surface = session.Read<Surface>(TextureSurfacePath(store, run, folder, texture));
        var uvs = session.Read<TextureCoordinates>(store.PathFor(folder + "/" + texture.UvsFile));
        using var atlas = File.OpenRead(store.PathFor(folder + "/atlas.jpg"));
        glb.Write(output, surface, uvs.Values, atlas);
    }

    public string ExportPath(IProjectStore store, RunData run)
    {
        return store.PathFor(StageFolder(run, "export") + "/cliff.glb");
    }

    public IReadOnlyList<CameraPose> Poses(IProjectStore store, RunData run)
    {
        if (run.Artifacts.TryGetValue("alignment", out var folder))
        {
            return json.Read<ReconstructionOverview>(store.PathFor(folder + "/scene.json")).Cameras;
        }

        if (run.Artifacts.TryGetValue("alignmentPreview", out var preview))
        {
            return alignment.Read(store.PathFor(preview + "/preview.bin"), summary: true).Scene.Cameras;
        }

        return [];
    }

    public StageCameraInputs CameraInputs(IProjectStore store, RunData run, string stage)
    {
        var folder = StageFolder(run, stage, allowPreview: true);
        switch (stage)
        {
            case "features":
            case "depth":
                return new StageCameraInputs(stage == "features" ? "feature photos" : "depth-map photos",
                    ImageUnits(store, run, stage, folder).Select(unit => unit.ImageId).Distinct().ToArray());
            case "matching":
                var pairs = MatchEntries(store, run).ToArray();
                return new StageCameraInputs("saved match pairs",
                    pairs.SelectMany(pair => new[] { pair.A, pair.B }).Distinct().ToArray(), pairs);
            case "alignment":
            case "export":
                return new StageCameraInputs("aligned camera poses", Poses(store, run).Select(camera => camera.ImageId).ToArray());
            case "cloud":
            case "fusion":
                return new StageCameraInputs(stage == "cloud" ? "dense-cloud depth inputs" : "upstream dense-cloud depth inputs",
                    Cloud(store, run, includePoints: false).ImageIds);
            case "mesh":
            case "texture":
                return new StageCameraInputs("saved face source photos", SurfaceSources(store, run, stage, folder));
            default:
                throw new EchoException("Unknown camera lookup stage.", 2);
        }
    }

    private IReadOnlyList<string> SurfaceSources(IProjectStore store, RunData run, string stage, string folder)
    {
        var path = stage == "texture"
            ? TextureSurfacePath(store, run, folder, json.Read<TextureResult>(store.PathFor(folder + "/texture.json")))
            : store.PathFor(folder + "/mesh.json");
        using var input = File.OpenRead(path);
        using var header = JsonDocument.Parse(input);
        var root = header.RootElement;
        var entries = root.TryGetProperty("images", out var images) ? images : root.GetProperty("sourceImages");
        return entries.EnumerateArray().Select(entry => entry.GetString()).OfType<string>()
            .Where(imageId => imageId.Length > 0).Distinct().ToArray();
    }

    private string TextureSurfacePath(IProjectStore store, RunData run, string folder, TextureResult texture)
    {
        return texture.MeshFile == null
            ? store.PathFor(StageFolder(run, "mesh") + "/mesh.json")
            : store.PathFor(folder + "/" + texture.MeshFile);
    }

    private string ImageArtifact(IProjectStore store, RunData run, string stage, string imageId, string filename)
    {
        var folder = StageFolder(run, stage, allowPreview: true);
        var units = ImageUnits(store, run, stage, folder);
        var unit = run.Artifacts.ContainsKey(stage)
            ? units.FirstOrDefault(candidate => candidate.ImageId == imageId)
            : units.LastOrDefault(candidate => candidate.ImageId == imageId);
        if (unit == null)
        {
            throw new EchoException($"Run {run.Id} has no saved {stage} data for image {imageId}.", 2);
        }

        return store.PathFor(unit.Artifact + "/" + filename);
    }

    private IEnumerable<ImagePreviewRecord> ImageUnits(IProjectStore store, RunData run, string stage, string folder)
    {
        if (!run.Artifacts.ContainsKey(stage))
        {
            var reader = new BinaryJournalReader<ImagePreviewRecord>(new ImagePreviewRecordCodec());
            using var journal = reader.OpenRead(store.PathFor(folder + "/units.bin"));
            if (journal != null)
            {
                foreach (var unit in reader.Read(journal))
                {
                    yield return unit;
                }
            }

            yield break;
        }

        var units = store.PathFor(folder + "/units.json");
        if (stage == "features")
        {
            foreach (var unit in json.Read<Dictionary<string, string>>(units))
            {
                yield return new ImagePreviewRecord(unit.Key, unit.Value);
            }

            yield break;
        }

        foreach (var unit in json.Read<List<string>>(units))
        {
            using var input = File.OpenRead(store.PathFor(unit + "/depth.json"));
            using var header = JsonDocument.Parse(input);
            var imageId = header.RootElement.GetProperty("imageId").GetString();
            if (imageId != null)
            {
                yield return new ImagePreviewRecord(imageId, unit);
            }
        }
    }

    private string StageFolder(RunData run, string stage, bool allowPreview = false)
    {
        if (run.Artifacts.TryGetValue(stage, out var folder))
        {
            return folder;
        }

        if (allowPreview && run.Artifacts.TryGetValue(stage + "Preview", out var preview))
        {
            return preview;
        }

        throw new EchoException($"Run {run.Id} has no saved {stage} artifact.", 2);
    }

    private bool IsPair(string a, string b, string firstId, string secondId)
    {
        return (a == firstId && b == secondId) || (a == secondId && b == firstId);
    }
}
