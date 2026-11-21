using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Export;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace Echo.Tests;

public sealed class GeographicFormatTests
{
    private readonly IGisExportService exporter;
    private readonly IGlbWriter models;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly ILasWriter las;
    private readonly ICloudBuilder clouds;
    private readonly IGeoPackageWriter packages;

    public GeographicFormatTests(IGisExportService exporter, IGlbWriter models, IProjectStoreFactory stores, IFileOperations files, ILasWriter las, ICloudBuilder clouds, IGeoPackageWriter packages)
    {
        this.exporter = exporter;
        this.models = models;
        this.stores = stores;
        this.files = files;
        this.las = las;
        this.clouds = clouds;
        this.packages = packages;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Saved GIS bundle opens as GeoPackage, LAS, 3D Tiles and Redpoint calibration", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-formats-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = stores.Open(root);
                store.Init("Geographic interchange");
                var origin = new GeoPosition { Latitude = -33.7, Longitude = 150.2, Altitude = 123, AltitudeDatum = "WGS84 ellipsoid" };
                var scene = new ReconstructionScene
                {
                    Origin = origin,
                    Frame = "local: x east, y up, z south; metres when georeferenced",
                    Cameras = [new CameraPose { ImageId = "photo", Centre = [0, 2, 0] }],
                    Points = [new CloudPoint { Position = [12, 7, -24], Colour = [255, 64, 0] }, new CloudPoint { Position = [-20, 0, 10], Colour = [1, 2, 3] }]
                };
                var surface = new Surface
                {
                    Vertices = [new double[] { -20, 0, -10 }, new double[] { 30, 5, -10 }, new double[] { 30, 12, 20 }, new double[] { -20, 0, 20 }],
                    Faces = [new[] { 0, 1, 2 }, new[] { 0, 2, 3 }]
                };
                files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), scene);
                files.AtomicData(store.PathFor("artifacts/mesh/test/mesh.json"), surface);
                Directory.CreateDirectory(store.PathFor("artifacts/export/test"));
                using var image = new SKBitmap(4, 4);
                image.Erase(SKColors.Orange);
                using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
                models.Write(store.PathFor("artifacts/export/test/cliff.glb"), surface,
                    [new double[] { 0, 0 }, new double[] { 1, 0 }, new double[] { 1, 1 }, new double[] { 0, 1 }], jpeg.ToArray());
                var runId = Guid.NewGuid().ToString("N");
                var snapshot = store.Load();
                snapshot.Images = [new Photo { Id = "photo", Name = "photo.jpg", Gps = origin }];
                var run = new RunData
                {
                    Id = runId,
                    Snapshot = snapshot,
                    Status = "completed",
                    Artifacts = new Dictionary<string, string>
                    {
                        ["alignment"] = "artifacts/alignment/test",
                        ["mesh"] = "artifacts/mesh/test",
                        ["export"] = "artifacts/export/test"
                    }
                };
                files.AtomicJson(store.PathFor($"runs/{runId}/run.json"), run);
                var output = Path.Combine(root, "geographic");
                var result = exporter.Export(store, runId, output);
                Check(result.Warnings.Count == 0, string.Join("; ", result.Warnings));
                CheckPackage(output);
                CheckLas(output, origin);
                CheckTiles(output, surface, origin);
                CheckRedpoint(output, origin);

                var cloud = new DenseCloudData
                {
                    ImageIds = ["photo"],
                    Samples = [new DenseSample(1, 2, 3, 4, 5, 6, 0, 1), new DenseSample(7, 8, 9, 10, 11, 12, 0, 1), new DenseSample(3, 4, 5, 6, 7, 8, 0, 1)]
                };
                Directory.CreateDirectory(store.PathFor("artifacts/cloud/test"));
                clouds.Write(store.PathFor("artifacts/cloud/test/cloud.ply"), cloud);
                files.AtomicJson(store.PathFor("artifacts/cloud/test/cloud.json"), clouds.Report(cloud));
                run.Artifacts["cloud"] = "artifacts/cloud/test";
                files.AtomicJson(store.PathFor($"runs/{runId}/run.json"), run);
                var denseOutput = Path.Combine(root, "dense-geographic");
                exporter.Export(store, runId, denseOutput);
                Check(BitConverter.ToUInt64(File.ReadAllBytes(Path.Combine(denseOutput, "cloud.las")), 247) == 3, "Saved dense points take precedence over the sparse reconstruction");
                Check(!File.Exists(Path.Combine(denseOutput, "sparse.las")), "Dense export is not duplicated as sparse data");

                var retained = Environment.GetEnvironmentVariable("ECHO_GIS_TEST_OUTPUT");
                if (!string.IsNullOrEmpty(retained))
                {
                    Directory.CreateDirectory(retained);
                    foreach (var file in Directory.GetFiles(output))
                    {
                        File.Copy(file, Path.Combine(retained, Path.GetFileName(file)), overwrite: true);
                    }
                }
                scene.Origin.AltitudeDatum = "unknown";
                Directory.Delete(store.PathFor("artifacts/alignment/test"), recursive: true);
                files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), scene);
                var horizontal = Path.Combine(root, "horizontal");
                var unknown = exporter.Export(store, runId, horizontal);
                Check(!File.Exists(Path.Combine(horizontal, "sparse.las")) && !File.Exists(Path.Combine(horizontal, "tileset.json")), "Unknown heights do not produce absolute Earth placement");
                Check(unknown.Warnings.Any(warning => warning.Contains("LAS omitted")), "Omission is reported");
                using var redpoint = JsonDocument.Parse(File.ReadAllText(Path.Combine(horizontal, "redpoint.georeference.json")));
                Check(redpoint.RootElement.GetProperty("geoReference").GetProperty("ReferencePoints")[0].GetProperty("Elevation").ValueKind == JsonValueKind.Null, "Unknown elevation remains null");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
        tests.Run("GeoPackage represents vertical triangles as lines while retaining mesh connectivity", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gpkg");
            try
            {
                packages.Write(path, new ReconstructionScene { Origin = new GeoPosition() }, new Surface
                {
                    Vertices = [new double[] { 0, 0, 0 }, new double[] { 0, 10, 0 }, new double[] { 0, 0, 20 }],
                    Faces = [new[] { 0, 1, 2 }]
                }, []);
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT geom,a,b,c FROM model";
                using var row = command.ExecuteReader();
                Check(row.Read(), "Vertical triangle retained");
                Check(BitConverter.ToUInt32((byte[])row[0], 9) == 2, "Horizontal geometry is a valid line rather than a zero-area polygon");
                Check(row.GetInt32(1) == 0 && row.GetInt32(2) == 1 && row.GetInt32(3) == 2, "Original triangle connectivity retained");
            }
            finally
            {
                File.Delete(path);
            }
        });
        tests.Run("LAS rejects non-finite points and ambiguous altitude", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".las");
            try
            {
                Reject(() => las.Write(path, new GeoPosition { Altitude = 5 }, [new GeographicCloudPoint(new V3(0, 0, 0), 0, 0, 0)]));
                Reject(() => las.Write(path, new GeoPosition { Altitude = 5, AltitudeDatum = "WGS84 ellipsoid" }, [new GeographicCloudPoint(new V3(double.NaN, 0, 0), 0, 0, 0)]));
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    private void CheckPackage(string directory)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "geography.gpkg"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        Check((string?)command.ExecuteScalar() == "ok", "SQLite integrity");
        command.CommandText = "SELECT count(*) FROM gpkg_geometry_columns WHERE srs_id=4326 AND z=0";
        Check((long)command.ExecuteScalar()! == 4, "Four horizontal feature layers");
        command.CommandText = "SELECT geom, local_east_m, local_south_m FROM vertices WHERE source_id='0'";
        using var rows = command.ExecuteReader();
        Check(rows.Read(), "Vertex exists");
        var geometry = (byte[])rows[0];
        Check(Encoding.ASCII.GetString(geometry, 0, 2) == "GP" && BitConverter.ToInt32(geometry, 4) == 4326, "GeoPackage header");
        var longitude = BitConverter.ToDouble(geometry, 13);
        var latitude = BitConverter.ToDouble(geometry, 21);
        Check(longitude < 150.2 && latitude > -33.7, "WKB longitude/latitude preserves east/south signs");
        Check(rows.GetDouble(1) == -20 && rows.GetDouble(2) == -10, "Local coordinates retained");
    }

    private void CheckLas(string directory, GeoPosition origin)
    {
        var bytes = File.ReadAllBytes(Path.Combine(directory, "sparse.las"));
        Check(Encoding.ASCII.GetString(bytes, 0, 4) == "LASF" && bytes[25] == 4 && bytes[104] == 7, "LAS 1.4 point format 7");
        Check(BitConverter.ToUInt16(bytes, 94) == 375 && BitConverter.ToUInt64(bytes, 247) == 2, "Header size and extended point count");
        var point = (int)BitConverter.ToUInt32(bytes, 96);
        Check(bytes.Length == point + 2 * 36, "Point record layout");
        Check(Encoding.ASCII.GetString(bytes, 429, point - 429).Contains("4978"), "Embedded geographic CRS");
        var actual = new V3(BitConverter.ToInt32(bytes, point) * 0.001 + BitConverter.ToDouble(bytes, 155),
            BitConverter.ToInt32(bytes, point + 4) * 0.001 + BitConverter.ToDouble(bytes, 163),
            BitConverter.ToInt32(bytes, point + 8) * 0.001 + BitConverter.ToDouble(bytes, 171));
        var expected = CameraGeometry.Ecef(GeographicCoordinates.FromLocal(new V3(12, 7, -24), origin));
        Check((actual - expected).Length < 0.001, "LAS ECEF coordinates agree within millimetre quantisation");
        Check(BitConverter.ToUInt16(bytes, point + 30) == 65535 && BitConverter.ToUInt16(bytes, point + 32) == 64 * 257, "16-bit colour");
    }

    private void CheckTiles(string directory, Surface surface, GeoPosition origin)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "tileset.json")));
        var root = document.RootElement.GetProperty("root");
        var m = root.GetProperty("transform").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var box = root.GetProperty("boundingVolume").GetProperty("box").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        foreach (var vertex in surface.Vertices)
        {
            double[] tile = [vertex[0], -vertex[2], vertex[1]];
            var actual = new V3(m[0] * tile[0] + m[4] * tile[1] + m[8] * tile[2] + m[12],
                m[1] * tile[0] + m[5] * tile[1] + m[9] * tile[2] + m[13],
                m[2] * tile[0] + m[6] * tile[1] + m[10] * tile[2] + m[14]);
            var expected = CameraGeometry.Ecef(GeographicCoordinates.FromLocal(new V3(vertex), origin));
            Check((actual - expected).Length < 1e-6, "glTF Y-up conversion followed by column-major tile transform matches ECEF");
            Check(Math.Abs(tile[0] - box[0]) <= box[3] && Math.Abs(tile[1] - box[1]) <= box[7] && Math.Abs(tile[2] - box[2]) <= box[11], "Tile bounds contain the model");
        }
    }

    private void CheckRedpoint(string directory, GeoPosition origin)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "redpoint.georeference.json")));
        var root = document.RootElement;
        Check(root.GetProperty("modelSha256").GetString() == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "cliff.glb")))), "Calibration identifies its exact model");
        var reference = root.GetProperty("geoReference");
        Check(reference.GetProperty("Version").GetInt32() == 1, "Redpoint contract casing");
        var points = reference.GetProperty("ReferencePoints");
        Check(points.GetArrayLength() == 5, "Non-collinear calibration samples");
        foreach (var point in points.EnumerateArray())
        {
            var local = new V3(point.GetProperty("SceneX").GetDouble(), point.GetProperty("SceneY").GetDouble(), point.GetProperty("SceneZ").GetDouble());
            var gps = new GeoPosition { Latitude = point.GetProperty("Latitude").GetDouble(), Longitude = point.GetProperty("Longitude").GetDouble(), Altitude = point.GetProperty("Elevation").GetDouble() };
            Check((CameraGeometry.ToLocal(gps, origin) - local).Length < 1e-6, "Redpoint samples round trip to the model frame");
        }
    }

    private void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (EchoException)
        {
            return;
        }
        throw new InvalidOperationException("Expected invalid geographic export to be rejected.");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
