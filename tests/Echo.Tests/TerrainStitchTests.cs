using System.Buffers.Binary;
using System.Text.Json;
using Echo.Cli;
using Echo.Export;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

public sealed class TerrainStitchTests
{
    private readonly ICommandApplication cli;
    private readonly IFileOperations files;
    private readonly IProjectStoreFactory stores;
    private readonly IMappingTools mapping;

    public TerrainStitchTests(ICommandApplication cli, IFileOperations files, IProjectStoreFactory stores, IMappingTools mapping)
    {
        this.cli = cli;
        this.files = files;
        this.stores = stores;
        this.mapping = mapping;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Builds a clipped terrain input with registered photo imagery before there are any wall runs", () =>
        {
            var root = Directory.CreateTempSubdirectory("echo-terrain-input-").FullName;
            try
            {
                CreateRasters(root);
                var polygon = Path.Combine(root, "area.geojson");
                File.WriteAllText(polygon, """{"type":"Polygon","coordinates":[[[-0.002,-0.002],[0,-0.002],[0,0.002],[-0.002,0.002],[-0.002,-0.002]]]}""");
                var overlay = Path.Combine(root, "overlay.tif");
                mapping.Run("gdal_create", "-of", "GTiff", "-outsize", "16", "16", "-ot", "Byte", "-bands", "4",
                    "-burn", "200", "-burn", "10", "-burn", "10", "-burn", "128", "-a_srs", "EPSG:4326",
                    "-a_ullr", "-0.0015", "0.0015", "0.0015", "-0.0015", overlay);
                var input = Path.Combine(root, "terrain.json");
                File.WriteAllBytes(input, JsonSerializer.SerializeToUtf8Bytes(new
                {
                    elevation = "terrain.tif",
                    imagery = "ortho.tif",
                    verticalDatum = "AHD",
                    footprint = "area.geojson",
                    photoOverlays = new[] { "overlay.tif" },
                    attribution = "Terrain source credit",
                    gridLongEdge = 32,
                    terrainTextureSize = 128
                }));
                var output = Path.Combine(root, "export");
                Check(cli.Run(["stitch-terrain", input, "--output", output]) == 0, "A raster-only input builds");
                var bytes = File.ReadAllBytes(Path.Combine(output, "cliff.glb"));
                var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
                using var doc = JsonDocument.Parse(bytes.AsMemory(20, length));
                var glb = doc.RootElement;
                Check(glb.GetProperty("asset").GetProperty("copyright").GetString() == "Terrain source credit", "Keeps attribution");
                var primitive = glb.GetProperty("meshes")[0].GetProperty("primitives")[0];
                var count = glb.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32();
                Check(count > 2500 && count < 3200, "Only the western half of the grid has triangles");
                var view = glb.GetProperty("bufferViews")[glb.GetProperty("images")[0].GetProperty("bufferView").GetInt32()];
                var start = 28 + length + view.GetProperty("byteOffset").GetInt32();
                using var image = SKBitmap.Decode(bytes.AsSpan(start, view.GetProperty("byteLength").GetInt32()).ToArray());
                var color = image.GetPixel(64, 64);
                Check(color.Red is > 100 and < 140 && color.Green is > 50 and < 90, "Partial alpha blends both images");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
        tests.Run("Stitch terrain keeps two original wall atlases and joins the source triangles to one ground mesh", () =>
        {
            var root = Directory.CreateTempSubdirectory("echo-stitch-test-").FullName;
            try
            {
                CreateRasters(root);
                var first = CreateWall(root, "east", 45);
                var second = CreateWall(root, "west", -45);
                var manifest = Path.Combine(root, "stitch.json");
                var output = Path.Combine(root, "output");
                Manifest(manifest, first, second, 0);

                Check(cli.Run(["stitch-terrain", manifest, "--output", output]) == 0, "Saved wall run stitching succeeds");
                VerifyModel(root, output);
                using var georeference = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "cliff.geo.json")));
                Check(georeference.RootElement.GetProperty("localToEcefRowMajor").GetArrayLength() == 16, "Preserves a common geographic frame");
                using var audit = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "terrain-stitch.json")));
                Check(audit.RootElement.GetProperty("walls").GetArrayLength() == 2, "Writes both seam measurements");

                var rejected = Path.Combine(root, "rejected");
                Manifest(manifest, first, second, 50);
                Check(cli.Run(["stitch-terrain", manifest, "--output", rejected]) == 2 && !Directory.Exists(rejected),
                    "A bad vertical seam is rejected without a partial output");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }

    private static void Manifest(string path, string first, string second, double offset)
    {
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
        {
            elevation = "terrain.tif",
            imagery = "ortho.tif",
            verticalDatum = "WGS84 ellipsoid",
            terrainTextureSize = 128,
            gridLongEdge = 64,
            maxSeamErrorMetres = 1,
            walls = new[] { new { project = "east.echo", run = first, heightOffsetMetres = offset },
                new { project = "west.echo", run = second, heightOffsetMetres = 0d } }
        }));
    }

    private void CreateRasters(string root)
    {
        var grid = Path.Combine(root, "flat.asc");
        File.WriteAllLines(grid, new[] { "ncols 32", "nrows 32", "xllcorner -0.0015", "yllcorner -0.0015", "cellsize 0.00009375", "NODATA_value -9999" }
            .Concat(Enumerable.Repeat(string.Join(' ', Enumerable.Repeat("110", 32)), 32)));
        mapping.Run("gdal_translate", "-q", "-a_srs", "EPSG:4326", grid, Path.Combine(root, "terrain.tif"));
        var ortho = Path.Combine(root, "ortho.jpg");
        Image(ortho, 32, new SKColor(35, 124, 40));
        mapping.Run("gdal_translate", "-q", "-a_srs", "EPSG:4326", "-a_ullr", "-0.0015", "0.0015", "0.0015", "-0.0015", ortho, Path.Combine(root, "ortho.tif"));
    }

    private string CreateWall(string root, string name, int x)
    {
        var store = stores.Open(Path.Combine(root, name + ".echo"));
        store.Init(name);
        const string mesh = "artifacts/mesh/example";
        const string texture = "artifacts/texture/example";
        const string alignment = "artifacts/alignment/example";
        files.AtomicData(store.PathFor(mesh + "/mesh.json"), new Surface
        {
            Vertices = [[x - 15, 0, -15], [x + 15, 0, -15], [x + 15, 0, 15], [x - 15, 0, 15], [x, 12, 0], [x, 4, 0]],
            Faces = [[0, 1, 5], [1, 4, 5], [4, 0, 5], [1, 2, 4], [2, 3, 4], [3, 0, 4]]
        });
        files.AtomicData(store.PathFor(texture + "/uvs.json"), new TextureCoordinates { Values = [[0, 0], [1, 0], [1, 1], [0, 1], [.5, .5], [.5, .28]] });
        files.AtomicJson(store.PathFor(texture + "/texture.json"), new TextureResult());
        Image(store.PathFor(texture + "/atlas.jpg"), 8192, new SKColor((byte)(x > 0 ? 192 : 110), 90, 80));
        files.AtomicData(store.PathFor(alignment + "/scene.json"), new ReconstructionScene
        {
            Origin = new GeoPosition { Latitude = 0, Longitude = 0, Altitude = 110, AltitudeDatum = "WGS84 ellipsoid" }
        });
        var id = Guid.NewGuid().ToString("N");
        store.SaveRun(new RunData
        {
            Id = id,
            Snapshot = store.Load(),
            Status = "completed",
            Artifacts = new Dictionary<string, string> { ["mesh"] = mesh, ["texture"] = texture, ["alignment"] = alignment }
        });
        return id;
    }

    private static void VerifyModel(string root, string output)
    {
        var bytes = File.ReadAllBytes(Path.Combine(output, "cliff.glb"));
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
        using var doc = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
        var binOffset = 28 + jsonLength;
        var model = doc.RootElement;
        var meshes = model.GetProperty("meshes");
        Check(meshes.GetArrayLength() == 1, "One mesh is exported");
        var primitives = meshes[0].GetProperty("primitives");
        Check(primitives.GetArrayLength() == 3, "Ground and two textured walls are retained");
        var images = model.GetProperty("images");
        var views = model.GetProperty("bufferViews");
        var accessors = model.GetProperty("accessors");
        var groundView = views[images[0].GetProperty("bufferView").GetInt32()];
        var groundOffset = binOffset + groundView.GetProperty("byteOffset").GetInt32();
        var groundLength = groundView.GetProperty("byteLength").GetInt32();
        using var ground = SKBitmap.Decode(bytes.AsSpan(groundOffset, groundLength).ToArray());
        Check(ground.Pixels.Any(pixel => pixel.Red > 100), "The terrain atlas blends colours from the wall along the transition");
        for (var index = 1; index <= 2; index++)
        {
            var source = Path.Combine(root, index == 1 ? "east.echo" : "west.echo", "artifacts/texture/example/atlas.jpg");
            var view = views[images[index].GetProperty("bufferView").GetInt32()];
            var offset = view.GetProperty("byteOffset").GetInt32();
            var count = view.GetProperty("byteLength").GetInt32();
            Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(bytes.AsSpan(binOffset + offset, count)),
                "The original 8K atlas bytes are preserved");
            Check(accessors[primitives[index].GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() == 18,
                "All original wall triangles remain, including the fold");
        }

        var parent = Enumerable.Range(0, accessors[0].GetProperty("count").GetInt32()).ToArray();
        var used = new HashSet<int>();
        foreach (var primitive in primitives.EnumerateArray())
        {
            Check(primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32() == 0, "All surfaces share vertex indices at their seam");
            var index = accessors[primitive.GetProperty("indices").GetInt32()];
            var view = views[index.GetProperty("bufferView").GetInt32()];
            var start = binOffset + view.GetProperty("byteOffset").GetInt32();
            var count = index.GetProperty("count").GetInt32();
            for (var corner = 0; corner < count; corner += 3)
            {
                var a = Vertex(corner);
                var b = Vertex(corner + 1);
                var c = Vertex(corner + 2);
                parent[Find(b)] = Find(a);
                parent[Find(c)] = Find(a);
                used.Add(a);
                used.Add(b);
                used.Add(c);
            }

            int Vertex(int corner) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(start + corner * 4, 4));
        }

        Check(used.Select(Find).Distinct().Count() == 1, "The resulting indexed triangles form one connected surface");

        int Find(int vertex)
        {
            while (parent[vertex] != vertex)
            {
                parent[vertex] = parent[parent[vertex]];
                vertex = parent[vertex];
            }

            return vertex;
        }
    }

    private static void Image(string path, int size, SKColor color)
    {
        using var bitmap = new SKBitmap(size, size);
        bitmap.Erase(color);
        using var image = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
