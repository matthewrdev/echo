using System.ComponentModel.Composition;
using Echo.Metadata;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class HeifImportTests
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly IMetadataReader metadataReader;

    [ImportingConstructor]
    public HeifImportTests(IProjectStoreFactory storeFactory, IMetadataReader metadataReader)
    {
        this.storeFactory = storeFactory;
        this.metadataReader = metadataReader;
    }

    internal void Run(TestSuite tests)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        tests.Run("HEIF import keeps original metadata and normalises full-resolution pixels", () =>
        {
            var root = Directory.CreateTempSubdirectory("echo-heif-").FullName;
            try
            {
                CheckImport(root);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    private void CheckImport(string root)
    {
        var input = Directory.CreateDirectory(Path.Combine(root, "camera photos")).FullName;
        var path = Path.Combine(input, "rotated.HEIC");
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oriented-photo.heic"));
        File.WriteAllBytes(path, bytes);

        var store = storeFactory.Open(Path.Combine(root, "project.echo"));
        store.Init();
        var photo = store.Import(input, "first", int.MaxValue).Single();
        Check(photo.Name == "rotated.HEIC" && photo.Width == 96 && photo.Height == 128,
            "Use the full 128×96 primary image, rotated to portrait");
        Check(photo.Camera == "Echo HEIF test camera" && photo.CapturedAt == "2026:03:07 07:11:43",
            "Read the original camera and capture date");
        Check(Math.Abs(photo.FocalPixels - 128 * 24.0 / 36) < 1e-10, "Use the original 35mm-equivalent focal length");
        Check(photo.Gps is { Latitude: -35.5, Longitude: 150.75, Altitude: -12 }, "Read signed GPS coordinates and altitude");

        var original = store.PathFor($"sources/{photo.Id}.original.heic");
        Check(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(original)), "Keep the original HEIC byte-for-byte");
        var grouped = metadataReader.Read(original, includeAllGroups: true);
        Check(grouped["XMP-dc:description[1]"].GetString() == "Echo HEIF test metadata",
            "Read grouped XMP from the retained original");

        using var processing = SKBitmap.Decode(store.PathFor(photo.Source));
        using var preview = SKBitmap.Decode(store.PathFor(photo.Preview));
        Check(processing is { Width: 96, Height: 128 } && preview is { Width: 96, Height: 128 },
            "Save oriented JPEG processing and preview images");
        var top = processing!.GetPixel(48, 32);
        var bottom = processing.GetPixel(48, 96);
        Check(top.Red > 200 && top.Blue < 50, "Rotate the red half to the top");
        Check(bottom.Blue > 200 && bottom.Red < 50, "Rotate the blue half to the bottom");

        File.Move(path, Path.Combine(input, "same photo.heif"));
        Check(store.Import(input, "second", int.MaxValue).Count == 0, "Deduplicate by original bytes across both extensions");
        Check(store.Load().Images.Single().Sets.SequenceEqual(new[] { "first", "second" }), "Track the reused HEIF in both sets");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
