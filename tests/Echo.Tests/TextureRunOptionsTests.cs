using Echo.Cli;
using Echo.Contracts;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

public sealed class TextureRunOptionsTests
{
    private readonly ICommandApplication application;
    private readonly IArgumentParser parser;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;
    private readonly IFileOperations files;

    public TextureRunOptionsTests(ICommandApplication application, IArgumentParser parser,
        IProjectStoreFactory stores, IRasterImageService images, IFileOperations files)
    {
        this.application = application;
        this.parser = parser;
        this.stores = stores;
        this.images = images;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Texture CLI options accept strengths and reject invalid or ambiguous values", () =>
        {
            var defaults = new Settings();
            Check(defaults.TextureColourEnhancement == .5 && defaults.TextureSeamBlendPixels == 4, "Both finishing operations default on");
            var enabled = TextureRunOptionParser.Parse(parser.Parse(["run", "start", "--colour-tuning", "on", "--edge-softening", "on"]))!;
            Check(enabled.ColourStrength == .5 && enabled.SeamWidth == 4, "On selects the documented defaults");
            var tuned = TextureRunOptionParser.Parse(parser.Parse(["run", "start", "--colour-tuning", ".7", "--edge-softening", "8"]))!;
            Check(tuned.ColourStrength == .7 && tuned.SeamWidth == 8, "Numeric strengths are retained");
            foreach (var value in new[] { "NaN", "Infinity", "-0.1", "1.1", "yes" })
            {
                Expect<EchoException>(() => TextureRunOptionParser.Parse(parser.Parse(["run", "start", "--colour-tuning", value])));
            }
            foreach (var value in new[] { "-1", "17", "2.5", "yes" })
            {
                Expect<EchoException>(() => TextureRunOptionParser.Parse(parser.Parse(["run", "start", "--edge-softening", value])));
            }
            Expect<EchoException>(() => parser.Parse(["run", "start", "--colour-tuning"]));
            Expect<EchoException>(() => parser.Parse(["run", "start", "--edge-softening", "on", "--edge-softening", "off"]));
        });
        tests.Run("CLI finishing overrides are frozen per run without editing the project or changing feature checkpoints", RunSnapshot);
    }

    private void RunSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "echo-texture-options-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(directory);
        store.Init("Texture run options");
        try
        {
            var photos = new List<Photo>();
            for (var index = 0; index < 2; index++)
            {
                using var bitmap = new SKBitmap(64, 64);
                bitmap.Erase(new SKColor((byte)(60 + index * 20), 100, 120));
                var path = $"source-{index}.png";
                images.Save(bitmap, store.PathFor(path), SKEncodedImageFormat.Png);
                photos.Add(new Photo
                {
                    Id = index.ToString(),
                    Name = path,
                    Source = path,
                    SourceHash = files.HashFile(store.PathFor(path)),
                    Width = 64,
                    Height = 64,
                    FocalPixels = 64
                });
            }
            store.Edit(project =>
            {
                project.Images = photos;
                project.Settings.FeatureWidth = 64;
                project.Settings.FeatureLimit = 32;
                project.Settings.DetectLines = false;
                project.Settings.ComputeBackend = ComputeBackend.Cpu;
            });
            var before = File.ReadAllBytes(store.PathFor("project.json"));
            Check(application.Run(["--project", directory, "run", "start", "--target", "features", "--colour-tuning", "off", "--edge-softening", "off"]) == 0,
                "CLI starts a run with both operations disabled");
            var disabled = store.GetRun();
            Check(disabled.Snapshot.Settings.TextureColourEnhancement == 0 && disabled.Snapshot.Settings.TextureSeamBlendPixels == 0,
                "The saved run records both overrides");
            Check(before.SequenceEqual(File.ReadAllBytes(store.PathFor("project.json"))), "Per-run options do not edit the project");
            var saved = File.ReadAllBytes(store.PathFor($"runs/{disabled.Id}/run.json"));
            Check(application.Run(["--project", directory, "run", "resume", disabled.Id, "--colour-tuning", "on"]) == 2,
                "Resume refuses to change frozen inputs");
            Check(saved.SequenceEqual(File.ReadAllBytes(store.PathFor($"runs/{disabled.Id}/run.json"))), "Rejected resume leaves its saved run unchanged");
            Check(application.Run(["--project", directory, "run", "start", "--target", "features"]) == 0, "The next run uses configured defaults");
            var enabled = store.GetRun();
            Check(enabled.Snapshot.Settings.TextureColourEnhancement == .5 && enabled.Snapshot.Settings.TextureSeamBlendPixels == 4,
                "Finishing defaults stay enabled after an off override");
            Check(enabled.Artifacts["features"] == disabled.Artifacts["features"], "Finishing controls preserve upstream checkpoints");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private void Expect<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(TException).Name);
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
