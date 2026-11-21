using System.Text.Json;
using Echo.Models;
using Echo.Infrastructure;
using Echo.Storage;

namespace Echo.Tests;

public sealed class SceneStorageTests
{
    private readonly IBulkArtifactStore artifacts;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;

    public SceneStorageTests(IBulkArtifactStore artifacts, IJsonCodec json, IFileOperations files)
    {
        this.artifacts = artifacts;
        this.json = json;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Binary scenes preserve exact point bits, observations, colour and metadata across pages", () => InDirectory(directory =>
        {
            var scene = new ReconstructionScene
            {
                Frame = "local metres",
                Origin = new GeoPosition { Latitude = -35, Longitude = 150 },
                Cameras = [new CameraPose { ImageId = "rock", FocalPixels = 2351.125 }],
                Unplaced = ["unplaced"],
                Warnings = ["warning"],
                Bundle = new BundleReport { Tracks = 123, FinalMedianError = .125 }
            };
            for (var index = 0; index < 4000; index++)
            {
                scene.Points.Add(new CloudPoint
                {
                    Position = [index + .125, -0d, double.Epsilon],
                    PixelA = index % 2 == 0 ? [Math.PI, 2267.5] : [],
                    PixelB = [4031.5, -double.Epsilon],
                    ImageA = "rock",
                    ImageB = "photo-\u00e9",
                    Error = double.MaxValue,
                    Colour = index % 2 == 0 ? [0, 128, 255] : null
                });
            }
            scene.Points.Add(new CloudPoint());
            var path = Path.Combine(directory, "scene.json");
            files.AtomicData(path, scene);
            using var session = artifacts.OpenSession(65536, 0);
            var paged = session.Read<ReconstructionScene>(path);
            Check(paged.Points is PagedList<CloudPoint>, "Scene must use the page cache");
            for (var index = scene.Points.Count - 1; index >= 0; index--)
            {
                var expected = scene.Points[index];
                var actual = paged.Points[index];
                Check(Bits(expected.Position).SequenceEqual(Bits(actual.Position)), "Position precision changed");
                Check(Bits(expected.PixelA).SequenceEqual(Bits(actual.PixelA)), "First observation changed");
                Check(Bits(expected.PixelB).SequenceEqual(Bits(actual.PixelB)), "Second observation changed");
                Check(BitConverter.DoubleToInt64Bits(expected.Error) == BitConverter.DoubleToInt64Bits(actual.Error), "Error changed");
                Check(expected.ImageA == actual.ImageA && expected.ImageB == actual.ImageB, "Photograph identity changed");
                Check(expected.Colour == null ? actual.Colour == null : expected.Colour.SequenceEqual(actual.Colour!), "Colour changed");
            }
            Check(session.Statistics.Evictions > 0 && session.Statistics.PeakResidentBytes == 65536, "Scene did not obey the page budget");
            var owned = json.Read<ReconstructionScene>(path);
            Check(owned.Points is List<CloudPoint> && owned.Points.Count == scene.Points.Count, "Owned read must survive its storage session");
            Check(owned.Bundle?.Tracks == 123 && owned.Cameras[0].FocalPixels == 2351.125, "Alignment metadata changed");
            Check(owned.Origin?.Latitude == -35 && owned.Unplaced.Single() == "unplaced" && owned.Warnings.Single() == "warning", "Scene overview changed");
        }));
        tests.Run("Camera-only scene reads do not open point buffers and old JSON scenes fail explicitly", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "scene.json");
            files.AtomicData(path, new ReconstructionScene { Cameras = [new CameraPose { ImageId = "p" }], Points = [new CloudPoint()] });
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var pointPath = Path.Combine(directory, document.RootElement.GetProperty("buffers").GetProperty("points").GetString()!);
            File.Delete(pointPath);
            var summary = json.Read<ReconstructionOverview>(path);
            Check(summary.Cameras.Single().ImageId == "p" && summary.ToScene().Points.Count == 0, "Camera summary accessed the point file");
            var failed = false;
            try
            {
                json.Read<ReconstructionScene>(path);
            }
            catch (FileNotFoundException)
            {
                failed = true;
            }
            Check(failed, "Full scene must reject a missing point buffer");
            File.WriteAllText(path, "{\"cameras\":[],\"points\":[]}");
            failed = false;
            try
            {
                json.Read<ReconstructionScene>(path);
            }
            catch (InvalidDataException)
            {
                failed = true;
            }
            Check(failed, "Legacy bulk JSON must require regeneration");
        }));
        tests.Run("Sparse point records reject corrupt photograph indices and flags", () =>
        {
            var codec = new CloudPointRecordCodec(["p"]);
            var bytes = new byte[codec.Size];
            codec.Encode(new CloudPoint { ImageA = "p", ImageB = "p" }, bytes);
            foreach (var offset in new[] { 64, 68, 72 })
            {
                var invalid = (byte[])bytes.Clone();
                invalid[offset] = 255;
                var rejected = false;
                try
                {
                    codec.Decode(invalid);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Check(rejected, "Corrupt point record was accepted");
            }
        });
    }

    private IEnumerable<long> Bits(double[] values) => values.Select(BitConverter.DoubleToInt64Bits);

    private void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "echo-scene-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
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
