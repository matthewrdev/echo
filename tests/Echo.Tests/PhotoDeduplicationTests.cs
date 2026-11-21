using Echo.Cli;
using Echo.Models;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

public sealed class PhotoDeduplicationTests
{
    private readonly ICommandApplication application;
    private readonly IPhotoDeduplicator deduplicator;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;

    public PhotoDeduplicationTests(IPhotoDeduplicator deduplicator, IProjectStoreFactory stores, IRasterImageService images, ICommandApplication application)
    {
        this.deduplicator = deduplicator;
        this.stores = stores;
        this.images = images;
        this.application = application;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Photo trim CLI previews, discards and restores without losing the original selection", () => WithProject(store =>
        {
            var first = SavePhoto(store, "a");
            var second = SavePhoto(store, "b");
            store.Edit(project => project.Images.AddRange([first, second]));
            RunCli(store, "images", "deduplicate");
            Check(store.Load().Images.All(photo => photo.Enabled), "CLI preview is read-only");
            RunCli(store, "images", "deduplicate", "--apply");
            Check(store.Load().Images.Single(photo => photo.Id == "b").Discarded, "CLI apply flags the duplicate");
            RunCli(store, "images", "restore", "b");
            RunCli(store, "images", "deduplicate", "--apply");
            Check(store.Load().Images.All(photo => photo.Enabled && !photo.Discarded), "Restored selection survives another CLI trim");
        }));
        tests.Run("Photo trimming keeps the sharper representative and preserves originals and saved runs", () => WithProject(store =>
        {
            var soft = SavePhoto(store, "soft", softened: true);
            var sharp = SavePhoto(store, "sharp");
            store.Edit(project => project.Images.AddRange([soft, sharp]));
            var original = File.ReadAllBytes(store.PathFor(soft.Source));
            var snapshot = store.Load();
            var plan = deduplicator.Plan(store);
            Check(plan.DuplicateCount == 1 && plan.Groups[0].Keep.Id == "sharp", "Keep the sharper photo");
            Check(store.Load().Images.All(photo => photo.Enabled), "Scanning must not edit the project");
            deduplicator.Apply(store, plan);
            Check(!store.Load().Images.Single(photo => photo.Id == "soft").Enabled, "Disable the redundant shot");
            Check(store.Load().Images.Single(photo => photo.Id == "sharp").Enabled, "Keep representative enabled");
            Check(snapshot.Images.All(photo => photo.Enabled), "Existing run snapshots remain unchanged");
            Check(original.SequenceEqual(File.ReadAllBytes(store.PathFor(soft.Source))), "Source bytes are retained");
            var discarded = store.Load().Images.Single(photo => photo.Id == "soft");
            Check(discarded.Discarded && discarded.DuplicateOf == "sharp", "Persist the discard flag and retained photo");
            var revision = store.Load().Revision;
            deduplicator.Apply(store, deduplicator.Plan(store));
            Check(store.Load().Revision == revision, "Repeating the trim is a no-op");
            deduplicator.Restore(store, "soft");
            var restored = store.Load().Images.Single(photo => photo.Id == "soft");
            Check(restored.Enabled && !restored.Discarded && restored.DuplicateOf == null, "Restore removes the discard state");
            Check(restored.KeepWhenTrimming, "Remember the user's explicit restoration");
            var nextPlan = deduplicator.Plan(store);
            Check(nextPlan.DuplicateCount == 0, "A later trim preserves both the restored photo and its original keeper");
        }));
        tests.Run("Photo trimming retains distinct camera baselines, rotations and disabled photos", () => WithProject(store =>
        {
            var first = SavePhoto(store, "first");
            var distant = SavePhoto(store, "distant");
            distant.Gps!.Latitude += .00002;
            var rotated = SavePhoto(store, "rotated");
            rotated.Attitude!.Pitch = 5;
            var later = SavePhoto(store, "later");
            later.CapturedAt = "2026:03:07 07:14:05";
            var zoomed = SavePhoto(store, "zoomed");
            zoomed.FocalPixels = 200;
            var disabled = SavePhoto(store, "disabled");
            disabled.Enabled = false;
            store.Edit(project => project.Images.AddRange([first, distant, rotated, later, zoomed, disabled]));
            var plan = deduplicator.Plan(store);
            Check(plan.EnabledBefore == 5 && plan.DuplicateCount == 0, "Keep stereo viewpoints and ignore disabled photos");
        }));
        tests.Run("Photo trimming compares every duplicate directly with its keeper without chaining drift", () => WithProject(store =>
        {
            var first = SavePhoto(store, "a");
            var middle = SavePhoto(store, "b");
            middle.Gps!.Latitude += .6 / 111195;
            var last = SavePhoto(store, "c");
            last.Gps!.Latitude += 1.2 / 111195;
            store.Edit(project => project.Images.AddRange([first, middle, last]));
            var plan = deduplicator.Plan(store);
            Check(plan.DuplicateCount == 1 && plan.Groups[0].Keep.Id == "a", "Slow camera drift must not collapse into one photo");
        }));
        tests.Run("Photo trimming retains all enabled hint references", () => WithProject(store =>
        {
            var first = SavePhoto(store, "a");
            var second = SavePhoto(store, "b");
            var third = SavePhoto(store, "c");
            store.Edit(project =>
            {
                project.Images.AddRange([first, second, third]);
                project.Hints.Add(new Hint { Type = "tie", ImageId = "b", OtherImageId = "c" });
            });
            var plan = deduplicator.Plan(store);
            Check(plan.DuplicateCount == 1 && plan.Groups[0].Duplicates[0].Id == "a", "Protect both ends of a tie hint");
        }));
        tests.Run("Photo trimming rejects stale plans and edits during processing", () => WithProject(store =>
        {
            var first = SavePhoto(store, "a");
            var second = SavePhoto(store, "b");
            store.Edit(project => project.Images.AddRange([first, second]));
            var plan = deduplicator.Plan(store);
            using (store.Lock("run"))
            {
                ExpectFailure(() => deduplicator.Apply(store, plan));
            }

            store.Edit(project => project.Images[0].Enabled = false);
            ExpectFailure(() => deduplicator.Apply(store, plan));
            Check(store.Load().Images[1].Enabled, "A stale plan cannot disable the remaining photo");
        }));
        tests.Run("Photo similarity handles absent GPS, blank images, exposure and local differences conservatively", () =>
        {
            var photo = new Photo { Width = 64, Height = 64 };
            var pixels = Enumerable.Range(0, 4096).Select(index => .3f + .2f * (float)Math.Sin(index * .71)).ToArray();
            var original = new PhotoSimilaritySample(photo, new GrayscaleImage(64, 64, pixels), new GrayscaleImage(64, 64, pixels), 0, false);
            var exposed = new PhotoSimilaritySample(photo, new GrayscaleImage(64, 64, pixels.Select(value => value + .01f).ToArray()), original.Image, 0, false);
            Check(PhotoSimilarity.Matches(original, exposed), "Near-identical shots without GPS tolerate slight exposure changes");
            var different = pixels.ToArray();
            for (var index = 0; index < 1024; index++)
            {
                different[index] = .9f;
            }

            Check(!PhotoSimilarity.Matches(original, new PhotoSimilaritySample(photo, new GrayscaleImage(64, 64, different), original.Image, 0, false)), "Keep changed local content");
            var blank = new PhotoSimilaritySample(photo, new GrayscaleImage(64, 64, new float[4096]), original.Image, 0, false);
            Check(!PhotoSimilarity.Matches(blank, blank), "Blank photos cannot establish duplicate viewpoints");
        });
    }

    private void RunCli(IProjectStore store, params string[] arguments)
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Check(application.Run(["--project", store.Root, .. arguments]) == 0, "CLI command failed");
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private Photo SavePhoto(IProjectStore store, string id, bool softened = false)
    {
        using var bitmap = new SKBitmap(128, 96);
        var random = new Random(47);
        var values = Enumerable.Range(0, bitmap.Width * bitmap.Height).Select(_ => random.Next(40, 210)).ToArray();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var index = y * bitmap.Width + x;
                var value = softened ? (byte)(values[index] * .98 + values[(index + 1) % values.Length] * .02) : (byte)values[index];
                bitmap.SetPixel(x, y, new SKColor(value, value, value));
            }
        }

        var path = $"sources/{id}.png";
        images.Save(bitmap, store.PathFor(path), SKEncodedImageFormat.Png);
        return new Photo
        {
            Id = id,
            Name = id,
            Source = path,
            Preview = path,
            Width = 128,
            Height = 96,
            CapturedAt = "2026:03:07 07:13:05",
            Gps = new GeoPosition { Latitude = -35, Longitude = 150 },
            Attitude = new Attitude()
        };
    }

    private void WithProject(Action<IProjectStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-duplicates-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = stores.Open(root);
            store.Init();
            action(store);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private void ExpectFailure(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected duplicate selection to reject the edit.");
        }
        catch (EchoException)
        {
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
