using Echo.Cli;
using Echo.Models;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

public sealed class SubjectDistanceRunOptionsTests
{
    private readonly ICommandApplication application;
    private readonly IArgumentParser parser;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;

    public SubjectDistanceRunOptionsTests(ICommandApplication application, IArgumentParser parser,
        IProjectStoreFactory stores, IRasterImageService images, IFileOperations files, IJsonCodec json)
    {
        this.application = application;
        this.parser = parser;
        this.stores = stores;
        this.images = images;
        this.files = files;
        this.json = json;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Subject distance CLI and settings validate automatic, off and finite positive custom limits", () =>
        {
            Check(new Settings().SubjectDistanceMode == SubjectDistanceMode.Auto, "Subject limiting defaults to automatic");
            Check(Parse("auto").Mode == SubjectDistanceMode.Auto && Parse("OFF").Mode == SubjectDistanceMode.Off, "Named controls parse without case sensitivity");
            var manual = Parse("125.5");
            Check(manual.Mode == SubjectDistanceMode.Manual && manual.MaximumDistance == 125.5, "A numeric value selects a custom cap");
            foreach (var value in new[] { "NaN", "Infinity", "0", "-1", "yes" })
            {
                ExpectFailure(() => Parse(value));
            }
            ExpectFailure(() => parser.Parse(["run", "start", "--subject-distance"]));
            ExpectFailure(() => parser.Parse(["run", "start", "--subject-distance", "off", "--subject-distance", "auto"]));
            var settings = new Settings();
            manual.Apply(settings);
            var copy = json.Clone(settings);
            Check(copy.SubjectDistanceMode == SubjectDistanceMode.Manual && copy.SubjectDistanceLimit == 125.5, "The setting survives project serialization");
            Check(json.Encode(settings).Contains("\"subjectDistanceMode\": \"manual\""), "Wire mode names are consistent with the native app");
            copy.SubjectDistanceLimit = double.NaN;
            ExpectFailure(copy.Validate);
        });
        tests.Run("Subject distance run overrides preserve the project, upstream checkpoints and frozen resume settings", RunSnapshot);
    }

    private SubjectDistanceRunOptions Parse(string value)
    {
        return SubjectDistanceRunOptionParser.Parse(parser.Parse(["run", "start", "--subject-distance", value]))!;
    }

    private void RunSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "echo-subject-options-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(directory);
        store.Init("Subject distance controls");
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
            Check(application.Run(["--project", directory, "run", "start", "--target", "features", "--subject-distance", "125"]) == 0,
                "A custom limit can start a run");
            var limited = store.GetRun();
            Check(limited.Snapshot.Settings.SubjectDistanceMode == SubjectDistanceMode.Manual
                && limited.Snapshot.Settings.SubjectDistanceLimit == 125, "The run snapshot records the override");
            Check(before.SequenceEqual(File.ReadAllBytes(store.PathFor("project.json"))), "The project is unchanged");
            var saved = File.ReadAllBytes(store.PathFor($"runs/{limited.Id}/run.json"));
            Check(application.Run(["--project", directory, "run", "resume", limited.Id, "--subject-distance", "off"]) == 2,
                "Resume cannot change the frozen subject limit");
            Check(saved.SequenceEqual(File.ReadAllBytes(store.PathFor($"runs/{limited.Id}/run.json"))), "Rejected resume leaves its snapshot unchanged");
            Check(application.Run(["--project", directory, "run", "start", "--target", "features"]) == 0, "A subsequent run uses the project defaults");
            var automatic = store.GetRun();
            Check(automatic.Snapshot.Settings.SubjectDistanceMode == SubjectDistanceMode.Auto, "The custom override does not persist into later runs");
            Check(automatic.Artifacts["features"] == limited.Artifacts["features"], "Distance controls preserve feature work");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private void ExpectFailure(Action action)
    {
        try
        {
            action();
        }
        catch (EchoException)
        {
            return;
        }
        throw new InvalidOperationException("Expected a rejected setting or argument");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
