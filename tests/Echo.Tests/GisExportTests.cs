using System.Text.Json;
using Echo.Export;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Metadata;
using Echo.Models;
using Echo.Pipeline.Exporting;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Tests;

public sealed class GisExportTests
{
    private readonly IGisWriter writer;
    private readonly IGisExportService exporter;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;

    public GisExportTests(IGisWriter writer, IGisExportService exporter, IProjectStoreFactory stores, IFileOperations files)
    {
        this.writer = writer;
        this.exporter = exporter;
        this.stores = stores;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Altitude inference distinguishes explicit modern references from ambiguous legacy EXIF", () =>
        {
            var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
                {"AbsoluteAltitude":"+87.202","GPSAltitude":87.202,"GPSAltitudeRef":0,"GPSMapDatum":"WGS-84","ExifVersion":"0230"}
                """)!;
            var position = new GeoPosition { Altitude = 87.202 };
            var legacy = AltitudeReferenceResolver.Resolve(position, metadata);
            Check(legacy.Datum == "unknown" && legacy.Source == "AbsoluteAltitude" && legacy.Evidence.Contains("Legacy"), "Legacy ambiguity is explained without dropping altitude");
            metadata["ExifVersion"] = JsonSerializer.SerializeToElement("0300");
            Check(AltitudeReferenceResolver.Resolve(position, metadata).Datum == "WGS84 ellipsoid", "Modern WGS84 ellipsoid is inferred");
            metadata["GPSAltitudeRef"] = JsonSerializer.SerializeToElement(2);
            Check(AltitudeReferenceResolver.Resolve(position, metadata).Datum == "mean sea level (geoid unspecified)", "Sea level does not imply a particular geoid");
            metadata["GPSAltitude"] = JsonSerializer.SerializeToElement(120);
            Check(AltitudeReferenceResolver.Resolve(position, metadata).Datum == "unknown", "Differing vendor altitude cannot borrow GPS reference");
            position.AltitudeDatum = "EGM96 geoid";
            Check(AltitudeReferenceResolver.Resolve(position, metadata).Datum == "EGM96 geoid", "Explicit reference takes precedence");
            position.Altitude = null;
            Check(AltitudeReferenceResolver.Resolve(position, metadata).Datum == "unknown", "Missing altitude remains missing");
        });
        tests.Run("Geographic transform inverts the saved WGS84 ECEF frame at southern, northern and equatorial sites", () =>
        {
            foreach (var latitude in new[] { -33.7, 0, 70 })
            {
                var origin = new GeoPosition { Latitude = latitude, Longitude = 150, Altitude = 123 };
                var geographic = new GeoPosition { Latitude = latitude + 0.001, Longitude = 150.002, Altitude = 153 };
                var local = CameraGeometry.ToLocal(geographic, origin);
                var restored = GeographicCoordinates.FromLocal(local, origin);
                Near(restored.Latitude, geographic.Latitude, 1e-9);
                Near(restored.Longitude, geographic.Longitude, 1e-9);
                Near(restored.Altitude!.Value, geographic.Altitude.Value, 1e-6);
            }
        });
        tests.Run("GIS preserves local geometry without mislabelling unknown heights as WGS84 Z", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-gis-" + Guid.NewGuid().ToString("N"));
            try
            {
                var scene = Scene();
                writer.Write(directory, scene, Surface(), [new Photo { Id = "camera", Name = "cliff.jpg", Gps = new GeoPosition { Latitude = -33, Longitude = 150, Altitude = 87.202 } }]);
                using var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "model.geojson")));
                var feature = model.RootElement.GetProperty("features")[0];
                var ring = feature.GetProperty("geometry").GetProperty("coordinates")[0];
                Check(ring.GetArrayLength() == 4 && ring[0].GetArrayLength() == 2, "Closed horizontal triangle");
                Check(ring[0].GetRawText() == ring[3].GetRawText(), "Ring is closed");
                Check(ring[0][0].GetDouble() > 149, "Longitude comes first");
                Check(feature.GetProperty("properties").GetProperty("local_up_max_m").GetDouble() == 7, "Local height retained");
                using var cameras = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "cameras.geojson")));
                Check(cameras.RootElement.GetProperty("features")[0].GetProperty("properties").GetProperty("name").GetString() == "cliff.jpg", "Camera identity retained");
                Check(File.ReadAllLines(Path.Combine(directory, "vertices.csv")).Length == 4, "Every vertex exported");
                using var photos = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "photo-locations.geojson")));
                var observation = photos.RootElement.GetProperty("features")[0];
                Near(observation.GetProperty("properties").GetProperty("photo_altitude_m").GetDouble(), 87.202, 1e-9);
                Check(observation.GetProperty("geometry").GetProperty("coordinates").GetArrayLength() == 2, "Recorded altitude is preserved independently of GeoJSON Z");
                using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "georeference.json")));
                Check(!reference.RootElement.GetProperty("geometryHasEllipsoidalHeight").GetBoolean(), "Unknown datum stays explicit");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
        tests.Run("Only explicitly ellipsoidal source heights become GeoJSON Z", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-gis-height-" + Guid.NewGuid().ToString("N"));
            try
            {
                var scene = Scene();
                scene.Origin!.AltitudeDatum = "WGS84 ellipsoid";
                writer.Write(directory, scene, null, []);
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "cameras.geojson")));
                var coordinates = document.RootElement.GetProperty("features")[0].GetProperty("geometry").GetProperty("coordinates");
                Check(coordinates.GetArrayLength() == 3, "Explicit ellipsoid enables Z");
                Near(coordinates[2].GetDouble(), 100, 1e-6);
                scene.Origin.Altitude = null;
                Check(!GeographicCoordinates.HasEllipsoidalHeight(scene.Origin), "Missing elevation never becomes zero");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
        tests.Run("GIS export opens saved runs without reconstruction and refuses to overwrite a destination", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-gis-project-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init("GIS test");
            try
            {
                files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), Scene());
                files.AtomicData(store.PathFor("artifacts/mesh/test/mesh.json"), Surface());
                files.AtomicJson(store.PathFor("runs/0123456789abcdef0123456789abcdef/run.json"), new RunData
                {
                    Id = "0123456789abcdef0123456789abcdef",
                    Snapshot = store.Load(),
                    Status = "completed",
                    Artifacts = new Dictionary<string, string> { ["alignment"] = "artifacts/alignment/test", ["mesh"] = "artifacts/mesh/test" }
                });
                var output = Path.Combine(root, "GIS");
                var result = exporter.Export(store, "0123456789abcdef0123456789abcdef", output);
                Check(result.Triangles == 1 && result.Cameras == 1, "Saved model and camera exported");
                var before = File.ReadAllText(Path.Combine(output, "model.geojson"));
                try
                {
                    exporter.Export(store, "0123456789abcdef0123456789abcdef", output);
                    throw new InvalidOperationException("Expected existing-folder rejection");
                }
                catch (EchoException)
                {
                    Check(File.ReadAllText(Path.Combine(output, "model.geojson")) == before, "Existing export unchanged");
                }

                var scene = Scene();
                scene.Origin = null;
                Directory.Delete(store.PathFor("artifacts/alignment/test"), recursive: true);
                files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), scene);
                var invalid = Path.Combine(root, "unplaced");
                try
                {
                    exporter.Export(store, "0123456789abcdef0123456789abcdef", invalid);
                    throw new InvalidOperationException("Expected missing-origin rejection");
                }
                catch (EchoException)
                {
                    Check(!Directory.Exists(invalid), "Failed exports do not leave a destination");
                    Check(!Directory.EnumerateDirectories(root, "*.tmp").Any(), "Staging cleaned up");
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    private ReconstructionScene Scene() => new()
    {
        Origin = new GeoPosition { Latitude = -33, Longitude = 150, Altitude = 100 },
        Cameras = [new CameraPose { ImageId = "camera", Centre = [0, 0, 0] }]
    };

    private Surface Surface() => new()
    {
        Vertices = [[0, 0, 0], [10, 5, 0], [0, 7, -10]],
        Faces = [[0, 1, 2]]
    };

    private void Near(double actual, double expected, double tolerance)
    {
        Check(Math.Abs(actual - expected) < tolerance, $"Expected {expected:R}, got {actual:R}");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
