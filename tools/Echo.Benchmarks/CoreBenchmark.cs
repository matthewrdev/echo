using System.ComponentModel.Composition;
using System.Diagnostics;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Projects;
using Echo.Reconstruction;
using Echo.Simplification;
using Echo.Texturing;
using Echo.Export;
using SkiaSharp;

namespace Echo.Benchmarks;

[Export]
public sealed class CoreBenchmark
{
    private readonly IFeatureMatcher matching;
    private readonly IReconstructor reconstruction;
    private readonly IEdgeCollapse simplification;
    private readonly ITextureBaker texturing;
    private readonly IGlbWriter export;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public CoreBenchmark(IFeatureMatcher matching, IEdgeCollapse simplification, ITextureBaker texturing, IGlbWriter export, IProjectStoreFactory stores, IRasterImageService images, IFileOperations files, IJsonCodec json, IReconstructor reconstruction)
    {
        this.matching = matching;
        this.reconstruction = reconstruction;
        this.simplification = simplification;
        this.texturing = texturing;
        this.export = export;
        this.stores = stores;
        this.images = images;
        this.files = files;
        this.json = json;
    }

    public int Run(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks core OUTPUT_DIRECTORY [TEXTURE_SIZE]");
            return 2;
        }
        var textureSize = args.Length == 2 ? int.Parse(args[1]) : 512;
        var store = stores.Open(Path.GetFullPath(args[0]));
        store.Init();
        var a = new FeatureSet { ImageId = "a" };
        var b = new FeatureSet { ImageId = "b" };
        var random = new Random(1701);
        for (var i = 0; i < 800; i++)
        {
            var descriptor = new float[64];
            for (var j = 0; j < descriptor.Length; j++)
            {
                descriptor[j] = (float)random.NextDouble();
            }
            a.Features.Add(new Feature { X = i, Descriptor = descriptor });
            b.Features.Add(new Feature { X = i + 10, Descriptor = (float[])descriptor.Clone() });
        }
        Measure("matching", () => files.Hash(matching.Match(a, b, new Settings(), default)));
        var photo = new Photo { Id = "a", Width = 256, Height = 192, FocalPixels = 256, Source = "photo.png" };
        var cameraA = new CameraPose { ImageId = "a" };
        var cameraB = new CameraPose { ImageId = "b", Centre = [1, .1, .05] };
        var observations = new List<Match>();
        for (var i = 0; i < 180; i++)
        {
            var point = new V3(random.NextDouble() * 4 - 2, random.NextDouble() * 3 - 1.5, 7 + random.NextDouble() * 5);
            var first = CameraGeometry.Project(photo, cameraA, point)!;
            var second = CameraGeometry.Project(photo, cameraB, point)!;
            observations.Add(new Match { A = [first[0], first[1]], B = i % 7 == 0 ? [random.NextDouble() * 256, random.NextDouble() * 192] : [second[0], second[1]] });
        }
        Measure("relative-pose", () => files.Hash(CameraGeometry.EstimateRelative(photo, photo, observations)
            ?? throw new EchoException("The reference camera pair could not be estimated.", 5)));
        MeasureAlignment();
        var surface = new Surface();
        const int columns = 41;
        const int rows = 31;
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                surface.Vertices.Add([(x - 20) * .1, (y - 15) * .1, 10 + Math.Sin(x * .2) * Math.Cos(y * .17) * .1]);
                if (x > 0 && y > 0)
                {
                    var index = y * columns + x;
                    surface.Faces.Add([index - columns - 1, index - 1, index]);
                    surface.Faces.Add([index - columns - 1, index, index - columns]);
                    surface.SourceImages.Add("a");
                    surface.SourceImages.Add("a");
                }
            }
        }
        Measure("simplification", () => files.Hash(simplification.Reduce(surface, 800, default)));
        using var source = new SKBitmap(256, 192);
        var pixels = new SKColor[256 * 192];
        for (var y = 0; y < 192; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                pixels[y * 256 + x] = new SKColor((byte)x, (byte)y, (byte)((x / 8 + y / 8) % 2 * 220));
            }
        }
        source.Pixels = pixels;
        images.Save(source, store.PathFor(photo.Source), SKEncodedImageFormat.Png);
        var project = new ProjectData { Images = [photo], Settings = new Settings { TextureSize = textureSize } };
        var scene = new ReconstructionScene { Cameras = [cameraA] };
        Measure("texture-export", () =>
        {
            var texturePath = store.PathFor("atlas.jpg");
            var texture = texturing.Bake(store, project, scene, surface, texturePath, default);
            files.AtomicJson(store.PathFor("texture.json"), texture);
            var output = store.PathFor("model.glb");
            export.Write(output, texture.Surface ?? throw new EchoException("The texture bake did not produce a surface.", 5),
                texture.Uvs, File.ReadAllBytes(texturePath));
            return files.HashFile(output);
        });
        return 0;
    }

    private void MeasureAlignment()
    {
        var origin = new GeoPosition { Latitude = -33, Longitude = 150, Altitude = 100 };
        var secondGps = new GeoPosition { Latitude = -33, Longitude = 150.00002, Altitude = 100 };
        var first = new Photo { Id = "a", Width = 1000, Height = 700, FocalPixels = 850, Gps = origin, Attitude = new Attitude() };
        var second = new Photo { Id = "b", Width = 1000, Height = 700, FocalPixels = 850, Gps = secondGps, Attitude = new Attitude() };
        var ground = new Photo { Id = "ground", Width = 1200, Height = 900, FocalPixels = 1000 };
        var poseA = new CameraPose { ImageId = "a", Rotation = CameraGeometry.AttitudeRotation(new Attitude()) };
        var poseB = new CameraPose { ImageId = "b", Centre = CameraGeometry.ToLocal(secondGps, origin).Array, Rotation = poseA.Rotation };
        var poseGround = new CameraPose { ImageId = "ground", Centre = [1, .4, -.2], Rotation = CameraGeometry.AttitudeRotation(new Attitude { Yaw = 5, Pitch = -3 }) };
        var firstPair = new ImagePair { A = "a", B = "b" };
        var groundPair = new ImagePair { A = "b", B = "ground" };
        var random = new Random(812);
        for (var index = 0; index < 70; index++)
        {
            var point = new V3(random.NextDouble() * 4 - 2, random.NextDouble() * 3 - 1, -8 - random.NextDouble() * 8);
            firstPair.Matches.Add(new Match { A = CameraGeometry.Project(first, poseA, point)![..2], B = CameraGeometry.Project(second, poseB, point)![..2] });
            groundPair.Matches.Add(new Match { A = CameraGeometry.Project(second, poseB, point)![..2], B = CameraGeometry.Project(ground, poseGround, point)![..2] });
        }
        var project = new ProjectData { Images = [first, second, ground] };
        List<ImagePair> pairs = [firstPair, groundPair];
        Measure("alignment", () => files.Hash(reconstruction.Align(project, pairs, default)));
    }

    private void Measure(string stage, Func<string> execute)
    {
        for (var iteration = 0; iteration < 6; iteration++)
        {
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            var hash = execute();
            watch.Stop();
            Console.WriteLine(json.Encode(new CoreMeasurement
            {
                Stage = stage,
                Iteration = iteration,
                Seconds = watch.Elapsed.TotalSeconds,
                AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                Hash = hash
            }, compact: true));
        }
    }
}
