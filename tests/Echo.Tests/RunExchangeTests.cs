using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Echo.Cli;
using Echo.DenseCloud;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Pipeline.Exporting;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

public sealed class RunExchangeTests
{
    private readonly ICommandApplication cli;
    private readonly ICloudBuilder clouds;
    private readonly IFileOperations files;
    private readonly IProjectStoreFactory stores;
    private readonly IPlyWriter plyWriter;

    public RunExchangeTests(ICommandApplication cli, ICloudBuilder clouds, IFileOperations files, IProjectStoreFactory stores, IPlyWriter plyWriter)
    {
        this.cli = cli;
        this.clouds = clouds;
        this.files = files;
        this.stores = stores;
        this.plyWriter = plyWriter;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Saved-run exchanges preserve mesh textures and coloured cloud coordinates", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-exchange-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init("Saved exchange");
            try
            {
                var run = CreateRun(store);
                var glb = Export(store, run, "mesh", "glb");
                var gltf = Export(store, run, "mesh", "gltf");
                var obj = Export(store, run, "mesh", "obj");
                var dae = Export(store, run, "mesh", "dae");
                var stl = Export(store, run, "mesh", "stl");
                var fbx = Export(store, run, "mesh", "fbx");
                var usdz = Export(store, run, "mesh", "usdz");

                Check(File.Exists(Path.Combine(glb, "cliff.glb")), "GLB exports directly from a saved Texture stage");
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(gltf, "cliff.gltf")));
                var buffer = document.RootElement.GetProperty("buffers")[0];
                Check(buffer.GetProperty("uri").GetString() == "cliff.bin"
                    && new FileInfo(Path.Combine(gltf, "cliff.bin")).Length == buffer.GetProperty("byteLength").GetInt32(), "glTF buffer is external and correctly sized");
                Check(document.RootElement.GetProperty("images")[0].GetProperty("uri").GetString() == "atlas.jpg", "glTF references its atlas");
                var atlas = File.ReadAllBytes(store.PathFor("artifacts/texture/exchange/atlas.jpg"));
                foreach (var folder in new[] { gltf, obj, dae, fbx })
                {
                    Check(atlas.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "atlas.jpg"))), "The saved atlas is preserved");
                }

                Check(File.ReadAllLines(Path.Combine(obj, "cliff.obj")).Contains("vt 0 1"), "OBJ converts V to a lower-left origin");
                Check(File.ReadAllText(Path.Combine(obj, "cliff.mtl")).Contains("map_Kd atlas.jpg"), "OBJ material references its atlas");
                var collada = XDocument.Load(Path.Combine(dae, "cliff.dae"));
                XNamespace schema = "http://www.collada.org/2005/11/COLLADASchema";
                Check(collada.Descendants(schema + "triangles").Single().Attribute("count")?.Value == "1", "COLLADA retains the triangle");
                Check(collada.Descendants(schema + "image").Single().Value == "atlas.jpg", "COLLADA references its atlas");
                var stlBytes = File.ReadAllBytes(Path.Combine(stl, "cliff.stl"));
                Check(stlBytes.Length == 134 && BitConverter.ToUInt32(stlBytes, 80) == 1, "STL contains one binary triangle");

                var fbxBytes = File.ReadAllBytes(Path.Combine(fbx, "cliff.fbx"));
                Check(fbxBytes.AsSpan(0, 23).SequenceEqual("Kaydara FBX Binary  \0\u001a\0"u8)
                    && BitConverter.ToUInt32(fbxBytes, 23) == 7400, "FBX is written in binary format directly by Echo");
                CheckUsdz(Path.Combine(usdz, "cliff.usdz"), atlas);
                foreach (var folder in new[] { glb, gltf, obj, dae, stl, fbx, usdz })
                {
                    using var georeference = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "cliff.geo.json")));
                    Check(georeference.RootElement.GetProperty("referenced").GetBoolean()
                        && georeference.RootElement.GetProperty("localToEcefRowMajor").GetArrayLength() == 16
                        && georeference.RootElement.GetProperty("axisConvention").GetString() == "X east, Y up, Z south",
                        "Each mesh carries its local-to-Earth transform and coordinate convention");
                }

                var ply = Export(store, run, "cloud", "ply");
                var pts = Export(store, run, "cloud", "pts");
                var xyz = Export(store, run, "cloud", "xyz");
                var dxf = Export(store, run, "cloud", "dxf");
                var las = Export(store, run, "cloud", "las");
                var geoLas = Export(store, run, "cloud", "geo-las");
                Check(File.ReadAllBytes(Path.Combine(ply, "cloud.ply")).SequenceEqual(File.ReadAllBytes(store.PathFor("artifacts/cloud/exchange/cloud.ply"))), "PLY is the original saved cloud");
                Check(File.ReadAllLines(Path.Combine(pts, "cloud.pts")).SequenceEqual(["1", "1 2 3 0 4 5 6"]), "PTS contains its point count and colour");
                Check(File.ReadAllText(Path.Combine(xyz, "cloud.xyz")).Trim() == "1 2 3 4 5 6", "XYZ contains local coordinates and colour");
                Check(File.ReadAllText(Path.Combine(dxf, "cloud.dxf")).Contains("\n420\n263430\n100\nAcDbPoint\n10\n1\n20\n-3\n30\n2\n"), "DXF stores true colour and Z-up coordinates");

                var localLas = File.ReadAllBytes(Path.Combine(las, "cloud.las"));
                var pointOffset = BitConverter.ToInt32(localLas, 96);
                Check(pointOffset == 227 && localLas[104] == 2 && localLas.Length == 253, "Local LAS uses the LAS 1.2 colour point layout");
                Check(BitConverter.ToInt32(localLas, pointOffset + 4) == -3000
                    && BitConverter.ToInt32(localLas, pointOffset + 8) == 2000
                    && BitConverter.ToUInt16(localLas, pointOffset + 20) == 4 * 257, "Local LAS stores Z-up coordinates and 16-bit colour");
                var earthLas = File.ReadAllBytes(Path.Combine(geoLas, "cloud.las"));
                Check(earthLas[104] == 7 && BitConverter.ToUInt64(earthLas, 247) == 1, "Geographic LAS uses the LAS 1.4 georeferenced layout");

                Check(cli.Run(["--project", root, "export", "mesh", "--format", "glb", "--run", run.Id, "--output", glb]) == 2,
                    "A second export cannot overwrite an existing package");
                run.Artifacts.Remove("texture");
                store.SaveRun(run);
                var unavailable = Path.Combine(root, "unavailable");
                Check(cli.Run(["--project", root, "export", "mesh", "--format", "obj", "--run", run.Id, "--output", unavailable]) == 2
                    && !Directory.Exists(unavailable)
                    && Directory.GetDirectories(root, "unavailable.*.tmp").Length == 0, "Unavailable formats leave no partial package");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    internal void RunHandoffTests(TestSuite tests)
    {
        if (OperatingSystem.IsMacOS())
        {
            tests.Run("Draco is opt-in and preserves a narrow UV triangle", () => WithRun((store, run) =>
            {
                files.AtomicData(store.PathFor("artifacts/mesh/draco/mesh.json"), new Surface
                {
                    Vertices = [[0, 0, 0], [2, 0, 0], [0, 2, 0], [3, 0, 0], [5, 0, 0], [3, 2, 0]],
                    Faces = [[0, 1, 2], [3, 4, 5]]
                });
                files.AtomicData(store.PathFor("artifacts/texture/draco/uvs.json"), new TextureCoordinates
                {
                    Values = [[0, 0], [1, 0], [0, 1], [.6, .6], [.60001, .6], [.6, .60001]]
                });
                files.AtomicJson(store.PathFor("artifacts/texture/draco/texture.json"), new TextureResult());
                File.Copy(store.PathFor("artifacts/texture/exchange/atlas.jpg"), store.PathFor("artifacts/texture/draco/atlas.jpg"));
                run.Artifacts["mesh"] = "artifacts/mesh/draco";
                run.Artifacts["texture"] = "artifacts/texture/draco";
                store.SaveRun(run);
                var ordinary = Export(store, run, "mesh", "glb");
                var compressed = Export(store, run, "mesh", "glb-draco");
                using var raw = OpenGlbJson(Path.Combine(ordinary, "cliff.glb"));
                using var draco = OpenGlbJson(Path.Combine(compressed, "cliff.glb"));
                Check(!raw.RootElement.TryGetProperty("extensionsRequired", out _), "Plain GLB needs no compression extension");
                Check(draco.RootElement.GetProperty("extensionsRequired")[0].GetString() == "KHR_draco_mesh_compression"
                    && draco.RootElement.GetProperty("accessors")[2].GetProperty("count").GetInt32() == 6,
                    "The verified Draco export contains both triangles");
                Check(File.Exists(Path.Combine(compressed, "cliff.geo.json"))
                    && !File.Exists(Path.Combine(compressed, "source.glb")), "Draco packages retain georeference and no temporary GLB");
                var recompressed = Path.Combine(store.Root, "recompressed.glb");
                Check(cli.Run(["compress-glb", Path.Combine(ordinary, "cliff.glb"), recompressed, "--jpeg-quality", "76"]) == 0
                    && File.Exists(recompressed), "An existing Echo GLB can be compressed with optional JPEG quality");
            }));
        }

        tests.Run("CLI exports triangle PLY, printable 3MF and saved-run photos", () => WithRun((store, run) =>
        {
            var ply = Export(store, run, "mesh", "ply");
            Check(File.ReadAllBytes(Path.Combine(ply, "cliff.ply")).SequenceEqual(
                File.ReadAllBytes(store.PathFor("artifacts/mesh/exchange/mesh.ply"))), "The mesh PLY retains triangle faces");
            var rejected = Path.Combine(store.Root, "open-mesh");
            Check(cli.Run(["--project", store.Root, "export", "mesh", "--format", "3mf", "--size-mm", "125", "--output", rejected]) == 2
                && !Directory.Exists(rejected), "Open meshes are rejected without leaving a 3MF package");

            // Keep the same geometric corner under distinct indices, as material seams do.
            files.AtomicData(store.PathFor("artifacts/mesh/closed/mesh.json"), new Surface
            {
                Vertices = [[0, 0, 0], [4, 0, 0], [0, 2, 0], [0, 0, -3], [0, 0, 0]],
                Faces = [[0, 1, 2], [4, 3, 1], [1, 3, 2], [2, 3, 0]]
            });
            run.Artifacts["mesh"] = "artifacts/mesh/closed";
            store.SaveRun(run);
            var print = Export(store, run, "mesh", "3mf", "--size-mm", "125");
            using var archive = ZipFile.OpenRead(Path.Combine(print, "cliff.3mf"));
            using var model = archive.GetEntry("3D/3dmodel.model")!.Open();
            var xml = XDocument.Load(model);
            XNamespace schema = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
            var vertices = xml.Descendants(schema + "vertex").ToArray();
            Check(xml.Root?.Attribute("unit")?.Value == "millimeter" && vertices.Length == 4
                && vertices.Max(vertex => (double)vertex.Attribute("x")!) == 125
                && vertices.Min(vertex => (double)vertex.Attribute("z")!) == 0, "3MF joins duplicate edges and writes the requested size in millimetres");
            Check(xml.Descendants(schema + "triangle").Count() == 4, "3MF contains the closed hull");
            Check(!File.Exists(Path.Combine(print, "cliff.geo.json")), "The scaled print does not use the source mesh transform");

            Directory.CreateDirectory(store.PathFor("sources"));
            File.WriteAllText(store.PathFor("sources/first.original.png"), "original");
            File.WriteAllText(store.PathFor("sources/second.jpg"), "normalized");
            run.Id = Guid.NewGuid().ToString("N");
            run.Snapshot.Images =
            [
                new Photo { Id = "first", Name = "cliff.PNG" },
                new Photo { Id = "second", Name = "cliff.PNG", Source = "sources/second.jpg" },
                new Photo { Id = "disabled", Name = "disabled.JPG", Enabled = false }
            ];
            store.SaveRun(run);
            var images = Path.Combine(store.Root, "photos");
            Check(cli.Run(["--project", store.Root, "export", "images", "--run", run.Id, "--output", images]) == 0,
                "The image handoff succeeds through its documented command");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(images, "photos.json")));
            var photos = manifest.RootElement.GetProperty("photos");
            Check(photos.GetArrayLength() == 2 && photos[0].GetProperty("original").GetBoolean()
                && !photos[1].GetProperty("original").GetBoolean(), "Image manifest distinguishes originals from normalized copies");
            Check(File.ReadAllText(Path.Combine(images, "images", "cliff.PNG")) == "original"
                && File.ReadAllText(Path.Combine(images, "images", "cliff.jpg")) == "normalized", "Images retain their saved bytes");

            var invalidCrs = Path.Combine(store.Root, "invalid-crs");
            Check(cli.Run(["--project", store.Root, "export", "cloud", "--format", "las", "--crs", "invalid", "--output", invalidCrs]) == 2
                && !Directory.Exists(invalidCrs), "An invalid EPSG code leaves no output");
        }));

        if (Environment.GetEnvironmentVariable("ECHO_TEST_PDAL") == "1")
        {
            tests.Run("CLI survey formats round-trip colour and projected coordinates through PDAL", () => WithRun((store, run) =>
            {
                var samples = new DenseCloudData
                {
                    Samples = [new DenseSample(1, 2, 3, 4, 5, 6, 0, 1), new DenseSample(19, 25, -7, 40, 50, 60, 0, 1)],
                    ImageIds = ["photo"]
                };
                clouds.Write(store.PathFor("artifacts/cloud/exchange/cloud.ply"), samples);
                PackedTestFiles.WriteCloud(store.PathFor("artifacts/cloud/exchange/cloud.bin"), clouds.Report(samples));

                foreach (var format in new[] { "las", "laz", "copc", "e57" })
                {
                    var options = format == "e57" ? Array.Empty<string>() : new[] { "--crs", "EPSG:7856" };
                    var folder = Export(store, run, "cloud", format, options);
                    var extension = format == "copc" ? "copc.laz" : format;
                    var path = Path.Combine(folder, "cloud." + extension);
                    using var verified = PdalPoints(path);
                    var points = verified.RootElement.GetProperty("points").GetProperty("point");
                    Check(points.GetArrayLength() == 2 && points.EnumerateArray().Any(point =>
                        point.GetProperty("Red").GetInt32() == 4 * 257 && point.GetProperty("Blue").GetInt32() == 6 * 257),
                        $"{format} preserves RGB samples");
                    var first = points.EnumerateArray().Single(point => point.GetProperty("Red").GetInt32() == 4 * 257);
                    var x = first.GetProperty("X").GetDouble();
                    var z = first.GetProperty("Z").GetDouble();
                    if (format == "e57")
                    {
                        Check(x == 1 && z == 2, "E57 keeps the local Z-up coordinates");
                    }
                    else
                    {
                        Check(x is > 200000 and < 300000 && Math.Abs(z - 125) < 0.01,
                            $"{format} stores the projected easting and retains the original ellipsoidal height");
                    }

                    var outputs = Directory.GetFiles(folder).Select(Path.GetFileName).ToHashSet();
                    var expected = format == "e57" ? new[] { "cloud.e57" } : new[] { "cloud." + extension, "cloud.crs.json" };
                    Check(outputs.SetEquals(expected), "The export folder contains only deliverables");
                    if (format != "e57")
                    {
                        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "cloud.crs.json")));
                        Check(reference.RootElement.GetProperty("verticalUnit").GetString() == "metre",
                            "The CRS sidecar records the units of retained ellipsoidal height");
                    }
                }
            }));
        }
    }

    private JsonDocument PdalPoints(string path)
    {
        var executable = Environment.GetEnvironmentVariable("ECHO_PDAL") ?? "pdal";
        var options = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true
        };
        foreach (var argument in new[] { "info", "--point", "0-1", path })
        {
            options.ArgumentList.Add(argument);
        }

        using var process = Process.Start(options)!;
        var result = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0, $"PDAL opened {path}");
        return JsonDocument.Parse(result);
    }

    private void WithRun(Action<IProjectStore, RunData> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-handoff-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init("Export handoff");
        try
        {
            check(store, CreateRun(store));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void CheckUsdz(string path, byte[] savedAtlas)
    {
        using var archive = ZipFile.OpenRead(path);
        Check(archive.Entries.Select(entry => entry.FullName).SequenceEqual(["cliff.usda", "atlas.jpg"]), "USDZ contains the scene first and embeds the texture");
        using var sceneStream = archive.Entries[0].Open();
        using var scene = new StreamReader(sceneStream);
        var layer = scene.ReadToEnd();
        Check(layer.StartsWith("#usda 1.0", StringComparison.Ordinal)
            && layer.Contains("asset inputs:file = @atlas.jpg@"), "USD scene references the embedded atlas");
        using var image = archive.Entries[1].Open();
        using var bytes = new MemoryStream();
        image.CopyTo(bytes);
        Check(savedAtlas.SequenceEqual(bytes.ToArray()), "USDZ packages the original saved texture");

        using var reader = new BinaryReader(File.OpenRead(path));
        foreach (var entry in archive.Entries)
        {
            var start = reader.BaseStream.Position;
            Check(reader.ReadUInt32() == 0x04034b50u, "USDZ local ZIP header present");
            reader.BaseStream.Position = start + 8;
            Check(reader.ReadUInt16() == 0, "USDZ entries are stored without compression");
            reader.BaseStream.Position = start + 26;
            var nameLength = reader.ReadUInt16();
            var extraLength = reader.ReadUInt16();
            var payload = start + 30 + nameLength + extraLength;
            Check(payload % 64 == 0, "USDZ entry payload is aligned to 64 bytes");
            reader.BaseStream.Position = payload + entry.Length;
        }
    }

    private RunData CreateRun(IProjectStore store)
    {
        var surface = new Surface
        {
            Vertices = [[0, 0, 0], [4, 0, 0], [1, 2, 3]],
            Faces = [[0, 1, 2]]
        };
        files.AtomicData(store.PathFor("artifacts/mesh/exchange/mesh.json"), surface);
        plyWriter.Write(store.PathFor("artifacts/mesh/exchange/mesh.ply"), surface.Vertices, surface.Faces);
        files.AtomicJson(store.PathFor("artifacts/texture/exchange/texture.json"), new TextureResult());
        files.AtomicData(store.PathFor("artifacts/texture/exchange/uvs.json"), new TextureCoordinates { Values = [[0, 0], [1, 0], [0, 1]] });
        using var image = new SKBitmap(4, 4);
        image.Erase(SKColors.Orange);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(store.PathFor("artifacts/texture/exchange/atlas.jpg"), jpeg.ToArray());

        var cloud = new DenseCloudData { Samples = [new DenseSample(1, 2, 3, 4, 5, 6, 0, 1)], ImageIds = ["photo"] };
        Directory.CreateDirectory(store.PathFor("artifacts/cloud/exchange"));
        clouds.Write(store.PathFor("artifacts/cloud/exchange/cloud.ply"), cloud);
        PackedTestFiles.WriteCloud(store.PathFor("artifacts/cloud/exchange/cloud.bin"), clouds.Report(cloud));

        files.AtomicData(store.PathFor("artifacts/alignment/exchange/scene.json"), new ReconstructionScene
        {
            Origin = new GeoPosition { Latitude = -33.7, Longitude = 150.2, Altitude = 123, AltitudeDatum = "WGS84 ellipsoid" }
        });
        var run = new RunData
        {
            Id = Guid.NewGuid().ToString("N"),
            Snapshot = store.Load(),
            Status = "completed",
            Artifacts = new Dictionary<string, string>
            {
                ["mesh"] = "artifacts/mesh/exchange",
                ["texture"] = "artifacts/texture/exchange",
                ["cloud"] = "artifacts/cloud/exchange",
                ["alignment"] = "artifacts/alignment/exchange"
            }
        };
        store.SaveRun(run);
        return run;
    }

    private string Export(IProjectStore store, RunData run, string group, string format, params string[] options)
    {
        var output = Path.Combine(store.Root, format);
        var code = cli.Run(["project", store.Root, "export", group, "--format", format, "--run", run.Id, "--output", output, .. options]);
        Check(code == 0, $"Exporting {format} succeeds");
        return output;
    }

    private JsonDocument OpenGlbJson(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 12;
        var length = reader.ReadInt32();
        reader.ReadUInt32();
        return JsonDocument.Parse(reader.ReadBytes(length));
    }

    private void Check(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }
}
