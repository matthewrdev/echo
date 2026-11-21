using Echo.Contracts;
using Echo.Depth;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.SceneSemantics;
using SkiaSharp;

namespace Echo.Benchmarks;

public sealed class DepthSkyBenchmark
{
    private readonly IRasterImageService images;
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly ISceneSegmenter segmenter;
    private readonly ISemanticModelProvider models;

    public DepthSkyBenchmark(IRasterImageService images, IProjectStoreFactory stores, IJsonCodec json,
        IFileOperations files, ISceneSegmenter segmenter, ISemanticModelProvider models)
    {
        this.images = images;
        this.stores = stores;
        this.json = json;
        this.files = files;
        this.segmenter = segmenter;
        this.models = models;
    }

    public int Run(string[] args)
    {
        if (args.Length is < 4 or > 5)
        {
            Console.Error.WriteLine("Usage: depth-sky PROJECT RUN IMAGE_NAME OUTPUT_DIRECTORY [CANDIDATE_DEPTH_JSON]");
            return 2;
        }
        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        var photo = run.Snapshot.Images.Single(photo => photo.Name == args[2]);
        var units = json.Read<Dictionary<string, string>>(store.PathFor(run.Artifacts["depthPreview"] + "/units.json"));
        var originalMap = json.Read<DepthMap>(store.PathFor(units[photo.Id] + "/depth.json"));
        var map = args.Length == 5 ? ReadCandidate(args[4]) : new DepthMap
        {
            ImageId = originalMap.ImageId,
            Width = originalMap.Width,
            Height = originalMap.Height,
            Depth = originalMap.Depth.ToArray(),
            Confidence = originalMap.Confidence.ToArray()
        };
        var model = models.EnsureAvailable(store.PathFor("models"), default);
        using var session = segmenter.Open(model);
        var semantics = session.Segment(store.PathFor(photo.Source), photo.Id, default);
        var acceptedBefore = originalMap.Depth.Count(value => value > 0);
        var candidateAccepted = map.Depth.Count(value => value > 0);
        var removed = DepthSkyFilter.Apply(map, semantics, default);
        var directory = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(directory);
        files.AtomicData(Path.Combine(directory, "depth.json"), map, default);
        files.AtomicData(Path.Combine(directory, "semantics.json"), semantics, default);
        using var original = images.Decode(store.PathFor(photo.Source));
        using var preview = images.Resize(original, 1200);
        var depths = originalMap.Depth.Where(depth => depth > 0).ToArray();
        var near = depths.Min();
        var far = depths.Max();
        RenderDepth(preview, originalMap, near, far, Path.Combine(directory, "before.png"));
        RenderDepth(preview, map, near, far, Path.Combine(directory, "after.png"));
        using var overlay = preview.Copy();
        var pixels = overlay.Pixels;
        for (var y = 0; y < overlay.Height; y++)
        {
            for (var x = 0; x < overlay.Width; x++)
            {
                if (!DepthSkyFilter.IsSky(semantics, (x + .5) / overlay.Width, (y + .5) / overlay.Height))
                {
                    continue;
                }
                var colour = pixels[y * overlay.Width + x];
                pixels[y * overlay.Width + x] = new SKColor((byte)(colour.Red / 2), (byte)((colour.Green + 255) / 2), (byte)((colour.Blue + 255) / 2));
            }
        }
        overlay.Pixels = pixels;
        images.Save(overlay, Path.Combine(directory, "sky-mask.png"), SKEncodedImageFormat.Png);
        files.AtomicJson(Path.Combine(directory, "report.json"), new
        {
            photo.Name,
            acceptedBefore,
            candidateAccepted,
            removed,
            acceptedAfter = candidateAccepted - removed
        });
        Console.WriteLine(json.Encode(new
        {
            photo.Name,
            acceptedBefore,
            candidateAccepted,
            removed,
            directory
        }));
        return 0;
    }

    private void RenderDepth(SKBitmap photograph, DepthMap map, float near, float far, string path)
    {
        using var output = photograph.Copy();
        var pixels = output.Pixels;
        for (var y = 0; y < output.Height; y++)
        {
            for (var x = 0; x < output.Width; x++)
            {
                var depth = map.Depth[(y * map.Height / output.Height) * map.Width + x * map.Width / output.Width];
                if (depth <= 0)
                {
                    continue;
                }
                var colour = SKColor.FromHsv(240 - 220 * Math.Clamp((depth - near) / Math.Max(.001f, far - near), 0, 1), 100, 100);
                var original = pixels[y * output.Width + x];
                pixels[y * output.Width + x] = new SKColor((byte)(original.Red * .45 + colour.Red * .55),
                    (byte)(original.Green * .45 + colour.Green * .55), (byte)(original.Blue * .45 + colour.Blue * .55));
            }
        }
        output.Pixels = pixels;
        images.Save(output, path, SKEncodedImageFormat.Png);
    }
    private DepthMap ReadCandidate(string path)
    {
        using var stream = File.OpenRead(path);
        return System.Text.Json.JsonSerializer.Deserialize<DepthMap>(stream, json.Options)
            ?? throw new InvalidDataException("Empty candidate depth map.");
    }

}
