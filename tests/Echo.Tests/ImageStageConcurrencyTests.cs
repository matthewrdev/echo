using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;
using Echo.Features;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Pipeline;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class ImageStageConcurrencyTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IRasterImageService images;
    private readonly IFeatureExtractor extractor;
    private readonly IFeatureMatcher matcher;
    private readonly IImagePairSelector selector;
    private readonly IOrderedWorkExecutor executor;

    [ImportingConstructor]
    public ImageStageConcurrencyTests(IProjectStoreFactory stores, IRunContextFactory contexts, IFileOperations files,
        IJsonCodec json, IRasterImageService images, IFeatureExtractor extractor, IFeatureMatcher matcher,
        IImagePairSelector selector, IOrderedWorkExecutor executor)
    {
        this.stores = stores;
        this.contexts = contexts;
        this.files = files;
        this.json = json;
        this.images = images;
        this.extractor = extractor;
        this.matcher = matcher;
        this.selector = selector;
        this.executor = executor;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Serial and concurrent image stages produce identical artifacts and ordered previews", () =>
        {
            foreach (var scaleInvariant in new[] { false, true })
            {
                WithProject(scaleInvariant, store =>
                {
                    var serial = RunStages(store, 1, 0, null);
                    var expected = ArtifactHashes(store);
                    Directory.Delete(store.PathFor("artifacts"), true);
                    var counting = new CountingFeatureJsonCodec(json);
                    var parallel = RunStages(store, 3, 16 * 1024 * 1024, counting);
                    Check(json.Encode(serial.Artifacts) == json.Encode(parallel.Artifacts), "Artifact fingerprints changed with concurrency/cache");
                    Check(json.Encode(expected) == json.Encode(ArtifactHashes(store)), "Serialized artifacts differ from serial execution");
                    Check(counting.FeatureReadCount == 4, "Six pairs should decode four images once, rather than twelve times");
                    CheckEvents(store, parallel);
                    CheckMatcherInputs(store, parallel);
                });
            }
        });
        tests.Run("Cancelled concurrent image stages resume their committed checkpoints", () => WithProject(true, store =>
        {
            var expected = RunStages(store, 1, 0, null);
            var expectedHashes = ArtifactHashes(store);
            Directory.Delete(store.PathFor("artifacts"), true);
            var run = NewRun(store);
            var plan = new FixedImageWorkPlanner(2, 16 * 1024 * 1024);
            var features = new FeatureStage(files, extractor, json, plan, executor);
            using (var cancelled = new CancellationTokenSource())
            {
                var context = contexts.Create(store, run, cancelled.Token, record =>
                {
                    if (EventType(record) == "imageCheckpoint")
                    {
                        cancelled.Cancel();
                    }
                });
                context.Start();
                ExpectCancellation(() => features.Execute(context));
            }
            Check(run.Artifacts.ContainsKey("featuresPreview") && !run.Artifacts.ContainsKey("features"), "Keep the committed preview without claiming stage completion");
            var savedFeatures = Directory.GetDirectories(store.PathFor("artifacts/feature-image")).Select(Path.GetFileName).ToHashSet();
            Check(savedFeatures.Count == 2, "Both completed workers should be reusable after cancellation");
            var resumed = contexts.Create(store, run, default, null);
            features.Execute(resumed);
            var matching = new MatchingStage(selector, files, matcher, plan, executor, new FeatureSetCacheFactory(json));
            using (var cancelled = new CancellationTokenSource())
            {
                var context = contexts.Create(store, run, cancelled.Token, record =>
                {
                    if (EventType(record) == "pairCheckpoint")
                    {
                        cancelled.Cancel();
                    }
                });
                ExpectCancellation(() => matching.Execute(context));
            }
            Check(!run.Artifacts.ContainsKey("matching"), "Cancelled matching must not publish its final index");
            Check(Directory.GetDirectories(store.PathFor("artifacts/match-pair")).Length == 2, "Keep completed pair checkpoints for resume");
            matching.Execute(contexts.Create(store, run, default, null));
            Check(json.Encode(expected.Artifacts) == json.Encode(run.Artifacts), "Resume changed final artifact fingerprints");
            Check(json.Encode(expectedHashes) == json.Encode(ArtifactHashes(store)), "Resume changed saved output");
            Check(!Directory.EnumerateFileSystemEntries(store.PathFor("work")).Any(), "No temporary worker directories may remain");
            var events = ReadEvents(store, run);
            Check(events.Any(record => record.GetProperty("type").GetString() == "artifactReused"), "Resume must reuse committed work");
        }));
    }

    private RunData RunStages(IProjectStore store, int workers, long cacheBytes, IJsonCodec? cacheJson)
    {
        var run = NewRun(store);
        var context = contexts.Create(store, run, default, null);
        context.Start();
        var plan = new FixedImageWorkPlanner(workers, cacheBytes);
        new FeatureStage(files, extractor, json, plan, executor).Execute(context);
        new MatchingStage(selector, files, matcher, plan, executor, new FeatureSetCacheFactory(cacheJson ?? json)).Execute(context);
        return run;
    }

    private RunData NewRun(IProjectStore store) => new() { Id = Guid.NewGuid().ToString("N"), Snapshot = store.Load(), Target = "matching" };

    private SortedDictionary<string, string> ArtifactHashes(IProjectStore store)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(store.PathFor("artifacts"), "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(store.Root, path), files.HashFile(path));
        }
        return result;
    }

    private void CheckEvents(IProjectStore store, RunData run)
    {
        var events = ReadEvents(store, run);
        var checkpoints = events.Where(record => record.GetProperty("type").GetString() == "imageCheckpoint").ToArray();
        Check(checkpoints.Select(record => record.GetProperty("payload").GetProperty("imageId").GetString()).SequenceEqual(new[] { "0", "1", "2", "3" }), "Image completion order changed");
        var previews = events.Where(record => record.GetProperty("type").GetString() == "checkpoint")
            .Select(record => record.GetProperty("payload")).Where(payload => payload.GetProperty("stage").GetString() == "featuresPreview").ToArray();
        for (var index = 0; index < previews.Length; index++)
        {
            var path = previews[index].GetProperty("artifact").GetString();
            var units = json.Read<Dictionary<string, string>>(store.PathFor(path + "/units.json"));
            Check(units.Keys.SequenceEqual(Enumerable.Range(0, index + 1).Select(i => i.ToString())), "Preview must contain an ordered prefix of committed images");
        }
        Check(previews.Length == 4, "Publish each completed image preview");
        var pairs = events.Where(record => record.GetProperty("type").GetString() == "pairCheckpoint").ToArray();
        Check(pairs.Select(record => record.GetProperty("payload").GetProperty("completed").GetInt32()).SequenceEqual(Enumerable.Range(1, 6)), "Pair completion counts must be ordered");
        Check(events.Select(record => record.GetProperty("sequence").GetInt64()).SequenceEqual(Enumerable.Range(1, events.Length).Select(i => (long)i)), "Concurrent reports must retain contiguous event sequences");
    }

    private void CheckMatcherInputs(IProjectStore store, RunData run)
    {
        var units = json.Read<Dictionary<string, string>>(store.PathFor(run.Artifacts["features"] + "/units.json"));
        var left = json.Read<FeatureSet>(store.PathFor(units["0"] + "/features.json"));
        var right = json.Read<FeatureSet>(store.PathFor(units["1"] + "/features.json"));
        var before = files.Hash(new[] { left, right });
        Parallel.For(0, 4, _ => matcher.Match(left, right, run.Snapshot.Settings, default));
        Check(before == files.Hash(new[] { left, right }), "Matching must treat shared feature inputs as immutable");
        Check(left.Features.Count > 0, "Fixture must exercise point matching");
    }

    private JsonElement[] ReadEvents(IProjectStore store, RunData run) => File.ReadLines(store.PathFor($"runs/{run.Id}/events.ndjson"))
        .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();

    private string? EventType(object record) => JsonSerializer.SerializeToElement(record, json.Options).GetProperty("type").GetString();

    private void WithProject(bool scaleInvariant, Action<IProjectStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-image-stages-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init();
        try
        {
            var project = store.Load();
            project.Settings = new Settings { FeatureWidth = 128, FeatureLimit = 100, LineLimit = 40, ScaleInvariantFeatures = scaleInvariant, MatchAllPairs = true, LineMinimumLength = 8 };
            using var bitmap = new SKBitmap(128, 96);
            var random = new Random(743);
            bitmap.Pixels = Enumerable.Range(0, 128 * 96).Select(_ => new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))).ToArray();
            images.Save(bitmap, store.PathFor("sources/pattern.png"), SKEncodedImageFormat.Png);
            // Reverse the snapshot order so the test also checks capture/name ordering.
            project.Images = Enumerable.Range(0, 4).Reverse().Select(index => new Photo
            {
                Id = index.ToString(),
                Name = index + ".png",
                Source = "sources/pattern.png",
                SourceHash = "pattern",
                Width = 128,
                Height = 96,
                FocalPixels = 128
            }).ToList();
            project.Hints.Add(new Hint { Id = "manual", Type = "tie", ImageId = "1", OtherImageId = "0", Pixel = [20, 30], OtherPixel = [21, 30] });
            files.AtomicJson(store.PathFor("project.json"), project);
            action(store);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private void ExpectCancellation(Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new InvalidOperationException("Expected cancellation");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
