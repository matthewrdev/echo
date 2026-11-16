using System.ComponentModel.Composition;
using System.Text;
using System.Text.Json;
using Echo.Contracts;
using Echo.Cli;
using Echo.Infrastructure;
using Echo.Texturing;

namespace Echo.Tests;

[Export]
public sealed class JsonDecodingTests
{
    private readonly IJsonCodec json;
    private readonly IArgumentParser arguments;

    [ImportingConstructor]
    public JsonDecodingTests(IJsonCodec json, IArgumentParser arguments)
    {
        this.json = json;
        this.arguments = arguments;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Inspection summary is a standalone CLI flag", () =>
        {
            var parsed = arguments.Parse(["--project", "saved.echo", "inspect", "--summary", "--run", "saved-run"]);
            Check(parsed.Options.ContainsKey("summary") && parsed.Get("run", null) == "saved-run", "Summary flag must not consume the next argument");
        });
        tests.Run("Generated JSON contracts preserve persisted values and serialization", () =>
        {
            RoundTrip(new ProjectData());
            RoundTrip(new RunData());
            RoundTrip(new Artifact());
            RoundTrip(new Settings());
            RoundTrip(new Settings { MeshIslandMode = IslandTrimMode.Main, MeshIslandMinimumAreaPercent = 2.5 });
            RoundTrip(new Settings { FusionIslandMode = IslandTrimMode.Small, FusionIslandMinimumSizePercent = 1.5, TextureSuppressSky = true, TextureSkyMarginPixels = 16 });
            RoundTrip(new Photo());
            RoundTrip(new List<Hint> { new Hint() });
            RoundTrip(new FeatureSet { ImageId = "photo", Features = [new Feature { X = 1.25, Y = 2.5, Descriptor = [0.1f, 0.2f] }] });
            RoundTrip(new ImagePair());
            RoundTrip(new ReconstructionScene());
            RoundTrip(new DenseCloudReport());
            RoundTrip(new bool[] { true, false });
            RoundTrip(new Settings { OptimiseVegetation = true, RecoverCliffDepth = true, VegetationMeshToleranceMultiplier = 3, VegetationTextureScale = .5 });
            RoundTrip(new TextureResult { MeshFile = "mesh.json", Uvs = [[0.25, 0.75]], Size = 256 });
            RoundTrip(new Dictionary<string, string> { ["image"] = "artifact/path" });
            RoundTrip(new List<string> { "artifact/path" });
        });
        tests.Run("Older projects preserve disabled island trimming until explicitly enabled", () =>
        {
            var settings = Read<Settings>("{}");
            Check(settings.MeshIslandMode == IslandTrimMode.Off && settings.MeshIslandMinimumAreaPercent == .1, "Missing fields retain compatible defaults");
            Check(settings.FusionIslandMode == IslandTrimMode.Off && settings.FusionIslandMinimumSizePercent == .1, "Early trimming remains opt-in");
            Check(!settings.OptimiseVegetation && !settings.RecoverCliffDepth, "Semantic passes remain opt-in for old projects");
            Check(!settings.TextureSuppressSky && settings.TextureSkyMarginPixels == 8, "Sky suppression remains opt-in");
            foreach (var mode in Enum.GetValues<IslandTrimMode>())
            {
                settings.MeshIslandMode = mode;
                settings.Validate();
                RoundTrip(settings);
            }
        });
        tests.Run("Typed island modes preserve legacy JSON and checkpoint inputs while rejecting unknown values", () =>
        {
            foreach (var mode in Enum.GetValues<IslandTrimMode>())
            {
                var wire = mode.ToString().ToLowerInvariant();
                var legacy = Read<Settings>($$"""{"fusionIslandMode":"{{wire}}","meshIslandMode":"{{wire}}"}""");
                Check(legacy.FusionIslandMode == mode && legacy.MeshIslandMode == mode, "Existing string settings decode as typed modes");
                Check(json.Clone(legacy).FusionIslandMode == mode, "Run snapshots preserve typed modes");
                using var document = JsonDocument.Parse(json.Encode(legacy));
                Check(document.RootElement.GetProperty("fusionIslandMode").GetString() == wire
                    && document.RootElement.GetProperty("meshIslandMode").GetString() == wire, "Saved mode names remain unchanged");
                Check(json.Encode(new
                {
                    fusionIslandMode = mode,
                    meshIslandMode = mode
                }) == json.Encode(new
                {
                    fusionIslandMode = wire,
                    meshIslandMode = wire
                }),
                    "Checkpoint input bytes remain identical after the type change");
            }
            foreach (var field in new[] { "fusionIslandMode", "meshIslandMode" })
            {
                foreach (var value in new[] { "\"invalid\"", "0", "999", "\"0\"", "null" })
                {
                    var rejected = false;
                    try
                    {
                        Read<Settings>($$"""{"{{field}}":{{value}}}""");
                    }
                    catch (JsonException)
                    {
                        rejected = true;
                    }
                    Check(rejected, $"Unsupported mode {value} must be rejected for {field}");
                }
            }
        });
        tests.Run("Inspection summaries omit bulk payload while retaining camera and cloud metadata", () =>
        {
            var scene = new ReconstructionScene { Frame = "local metres", Cameras = [new CameraPose { ImageId = "p" }], Points = [new CloudPoint()], Warnings = ["saved warning"] };
            var overview = Read<ReconstructionOverview>(json.Encode(scene)).ToScene();
            Check(overview.Points.Count == 0 && overview.Cameras.Single().ImageId == "p" && overview.Warnings.Single() == "saved warning", "Scene summary");
            var cloud = new DenseCloudReport { InputSamples = 100, RetainedSamples = 50, Preview = [new CloudPoint()], ImageIds = ["p"] };
            var report = Read<DenseCloudOverview>(json.Encode(cloud)).ToReport();
            Check(report.Preview.Count == 0 && report.RetainedSamples == 50 && report.InputSamples == 100 && report.ImageIds.Single() == "p", "Cloud summary");
        });
    }

    private void RoundTrip<T>(T value)
    {
        var encoded = json.Encode(value);
        var expected = JsonSerializer.Deserialize<T>(encoded, json.Options)!;
        var actual = Read<T>(encoded);
        Check(json.Encode(actual) == json.Encode(expected), $"Generated {typeof(T).Name} changed the saved contract: {json.Encode(actual)} != {json.Encode(expected)}");
    }

    private T Read<T>(string encoded)
    {
        var path = Path.Combine(Path.GetTempPath(), "echo-json-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, encoded);
            return json.Read<T>(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
