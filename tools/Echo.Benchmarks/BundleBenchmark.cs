using System.Diagnostics;
using Echo.Models;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Optimization;

namespace Echo.Benchmarks;

public sealed class BundleBenchmark
{
    private readonly IBundleAdjuster bundle;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;

    public BundleBenchmark(IBundleAdjuster bundle, IFileOperations files, IJsonCodec json)
    {
        this.bundle = bundle;
        this.files = files;
        this.json = json;
    }

    public int Run()
    {
        var project = new ProjectData { Settings = new Settings { BundleIterations = 25 } };
        var cameras = new List<CameraPose>();
        var random = new Random(615);
        var points = new V3[120];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new V3(random.NextDouble() * 6, random.NextDouble() * 4 - 2, 14 + random.NextDouble() * 8);
        }

        var pixels = new double[12][][];
        for (var i = 0; i < 12; i++)
        {
            var photo = new Photo { Id = $"camera-{i}", Width = 1000, Height = 700, FocalPixels = 850 };
            var camera = new CameraPose { ImageId = photo.Id, Centre = [i * .45, Math.Sin(i * .3) * .2, 0], FocalPixels = 850 };
            project.Images.Add(photo);
            cameras.Add(camera);
            pixels[i] = new double[points.Length][];
            for (var p = 0; p < points.Length; p++)
            {
                var projected = CameraGeometry.Project(photo, camera, points[p])!;
                pixels[i][p] = [projected[0] + (random.NextDouble() - .5) * .6, projected[1] + (random.NextDouble() - .5) * .6];
            }
        }

        var pairs = new List<ImagePair>();
        for (var i = 0; i < cameras.Count - 1; i++)
        {
            var pair = new ImagePair { A = cameras[i].ImageId, B = cameras[i + 1].ImageId };
            for (var p = 0; p < points.Length; p++)
            {
                pair.Matches.Add(new Match { A = pixels[i][p], B = pixels[i + 1][p], Manual = true });
            }

            pairs.Add(pair);
        }

        for (var iteration = 0; iteration < 6; iteration++)
        {
            var scene = new ReconstructionScene();
            for (var i = 0; i < cameras.Count; i++)
            {
                var camera = cameras[i];
                scene.Cameras.Add(new CameraPose
                {
                    ImageId = camera.ImageId,
                    Centre = [camera.Centre[0], camera.Centre[1] + (i < 2 ? 0 : Math.Sin(i) * .02), 0],
                    Rotation = (double[])camera.Rotation.Clone(),
                    FocalPixels = 850
                });
            }

            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            bundle.Refine(project, scene, pairs, default);
            watch.Stop();
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            Console.WriteLine(json.Encode(new
            {
                iteration,
                seconds = watch.Elapsed.TotalSeconds,
                allocatedBytes = allocated,
                hash = files.Hash(scene),
                scene.Bundle
            }));
        }

        return 0;
    }
}
