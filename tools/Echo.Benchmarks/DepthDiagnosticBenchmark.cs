using Echo.Models;
using Echo.Depth;
using Echo.Geometry;
using Echo.Features;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Benchmarks;

public sealed class DepthDiagnosticBenchmark
{
    private readonly IRasterImageService images;
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;

    public DepthDiagnosticBenchmark(IRasterImageService images, IProjectStoreFactory stores, IJsonCodec json)
    {
        this.images = images;
        this.stores = stores;
        this.json = json;
    }

    public int Run(string[] args)
    {
        if (args.Length is < 3 or > 4)
        {
            Console.Error.WriteLine("Usage: depth-diagnostic PROJECT RUN IMAGE_NAME [CANDIDATE_DEPTH_JSON]");
            return 2;
        }
        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        var project = run.Snapshot;
        var scene = json.Read<ReconstructionScene>(store.PathFor(run.Artifacts["alignment"] + "/scene.json"));
        var photo = project.Images.Single(photo => photo.Name == args[2]);
        var camera = scene.Cameras.Single(camera => camera.ImageId == photo.Id);
        var range = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
        var selected = DepthNeighbourSelector.Select(project, scene, camera, range, project.Settings.DepthNeighbours, default);
        var estimator = new DepthEstimator(images, []);
        var units = json.Read<Dictionary<string, string>>(store.PathFor(run.Artifacts["depthPreview"] + "/units.json"));
        var verification = selected.Select(other => new DepthView(project.Images.Single(item => item.Id == other.ImageId), other,
            json.Read<DepthMap>(store.PathFor(units[other.ImageId] + "/depth.json")))).ToArray();
        if (args.Length == 4)
        {
            CompareSaved(photo, camera, verification, json.Read<DepthMap>(store.PathFor(units[photo.Id] + "/depth.json")), "saved");
            CompareSaved(photo, camera, verification, ReadCandidate(args[3]), "candidate");
            return 0;
        }
        foreach (var width in new[] { 640, 2048 })
        {
            using var bitmap = images.Decode(store.PathFor(photo.Source));
            var reference = images.ResizeGray(bitmap, width);
            var referenceView = new DepthView(photo, camera, new DepthMap { Width = reference.Width, Height = reference.Height, Depth = new float[reference.Width * reference.Height] });
            var focal = CameraGeometry.Focal(photo, camera) * reference.Width / photo.Width;
            var right = CameraGeometry.Rotate(camera.Rotation, new V3(1 / focal, 0, 0));
            var down = CameraGeometry.Rotate(camera.Rotation, new V3(0, 1 / focal, 0));
            var views = selected.Select(other =>
            {
                var otherPhoto = project.Images.Single(item => item.Id == other.ImageId);
                using var otherBitmap = images.Decode(store.PathFor(otherPhoto.Source));
                var gray = images.ResizeGray(otherBitmap, width);
                var inverse = CameraGeometry.Transpose(other.Rotation);
                return new StereoView
                {
                    Centre = new V3(other.Centre),
                    WorldToCamera = inverse,
                    Width = gray.Width,
                    Height = gray.Height,
                    Focal = CameraGeometry.Focal(otherPhoto, other) * gray.Width / otherPhoto.Width,
                    Gray = gray.Pixels,
                    PatchRight = CameraGeometry.Rotate(inverse, right),
                    PatchDown = CameraGeometry.Rotate(inverse, down)
                };
            }).ToArray();
            foreach (var spacing in new[] { 1, 2 })
            {
                foreach (var sigma in new[] { 0.0, .7, 1.2 })
                {
                    var referencePixels = Smooth(reference.Pixels, reference.Width, reference.Height, sigma);
                    var sampledViews = views.Select(view => new StereoView
                    {
                        Centre = view.Centre,
                        WorldToCamera = view.WorldToCamera,
                        Width = view.Width,
                        Height = view.Height,
                        Focal = view.Focal,
                        Gray = Smooth(view.Gray, view.Width, view.Height, sigma),
                        PatchRight = view.PatchRight * spacing,
                        PatchDown = view.PatchDown * spacing
                    }).ToArray();
                    foreach (var planeCount in new[] { 192 })
                    {
                        var hypotheses = Enumerable.Range(0, planeCount).Select(index => 1 / (1 / range.Near + (1 / range.Far - 1 / range.Near) * index / (planeCount - 1))).ToArray();
                        var costs = new double[planeCount];
                        var patch = new double[25];
                        var lowTexture = 0;
                        var noMatch = 0;
                        var ambiguous = 0;
                        var accepted = 0;
                        var supported = 0;
                        var total = 0;
                        for (var y = 10; y < reference.Height - 10; y += Math.Max(1, width / 40))
                        {
                            for (var x = 10; x < reference.Width - 10; x += Math.Max(1, width / 40))
                            {
                                total++;
                                var norm = Normalize(referencePixels, reference.Width, x, y, patch, spacing);
                                if (norm < .0008)
                                {
                                    lowTexture++;
                                    continue;
                                }
                                var ray = CameraGeometry.Rotate(camera.Rotation, new V3((x - reference.Width * .5) / focal, (y - reference.Height * .5) / focal, 1));
                                if (estimator.SearchDepth(sampledViews, new V3(camera.Centre), ray, patch, norm, hypotheses, costs, true, default, out var depth, out var best))
                                {
                                    accepted++;
                                    if (CliffRecoverySearch.Supported(referenceView, verification, referenceView.Point(x, y, depth), x, y))
                                    {
                                        supported++;
                                    }
                                }
                                else if (best >= .18)
                                {
                                    noMatch++;
                                }
                                else
                                {
                                    ambiguous++;
                                }
                            }
                        }
                        Console.WriteLine(json.Encode(new
                        {
                            photo.Name,
                            width,
                            spacing,
                            sigma,
                            planeCount,
                            total,
                            lowTexture,
                            noMatch,
                            ambiguous,
                            accepted,
                            supported,
                            range
                        }));
                    }
                }
            }
        }
        return 0;
    }

    private float[] Smooth(float[] values, int width, int height, double sigma)
    {
        if (sigma == 0)
        {
            return values;
        }
        var result = new float[values.Length];
        GaussianBlur.Apply(values, result, width, height, sigma, default);
        return result;
    }

    private void CompareSaved(Photo photo, CameraPose camera, DepthView[] verification, DepthMap map, string label)
    {
        var view = new DepthView(photo, camera, map);
        var accepted = 0;
        var supported = 0;
        var total = 0;
        const int comparisonWidth = 1024;
        var scale = map.Width / (double)comparisonWidth;
        var comparisonHeight = (int)Math.Round(map.Height / scale);
        // Compare the same image locations and angular reprojection tolerance.
        // One pixel at each native width would penalize higher resolutions.
        for (var sampleY = 10; sampleY < comparisonHeight - 10; sampleY += 16)
        {
            for (var sampleX = 10; sampleX < comparisonWidth - 10; sampleX += 16)
            {
                var x = (int)Math.Round(sampleX * scale);
                var y = (int)Math.Round(sampleY * scale);
                total++;
                var depth = map.DepthAt(y * map.Width + x);
                if (depth <= 0)
                {
                    continue;
                }
                accepted++;
                if (SupportedAtCommonResolution(view, verification, view.Point(x, y, depth), x, y, scale))
                {
                    supported++;
                }
            }
        }
        Console.WriteLine(json.Encode(new
        {
            photo.Name,
            label,
            comparisonWidth,
            total,
            accepted,
            supported,
            coverage = map.Depth.Count(value => value > 0) / (double)map.SampleCount
        }));
    }

    private bool SupportedAtCommonResolution(DepthView reference, DepthView[] neighbours, V3 point, int x, int y, double tolerance)
    {
        var support = 0;
        foreach (var neighbour in neighbours)
        {
            var cosine = Math.Clamp((point - reference.Centre).Unit.Dot((point - neighbour.Centre).Unit), -1, 1);
            var usefulParallax = Math.Acos(cosine) >= .5 * Math.PI / 180;
            var agrees = usefulParallax && neighbour.TrySurfacePoint(point, .005, out var surface)
                && reference.ReprojectsTo(surface, x, y, tolerance);
            if (agrees && ++support >= 2)
            {
                return true;
            }
        }
        return false;
    }

    private double Normalize(float[] values, int width, int x, int y, double[] patch, int spacing)
    {
        var index = 0;
        double sum = 0;
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                patch[index] = values[(y + dy * spacing) * width + x + dx * spacing];
                sum += patch[index++];
            }
        }
        double norm = 0;
        for (index = 0; index < patch.Length; index++)
        {
            patch[index] -= sum / patch.Length;
            norm += patch[index] * patch[index];
        }
        return norm;
    }
    private DepthMap ReadCandidate(string path)
    {
        using var stream = File.OpenRead(path);
        return System.Text.Json.JsonSerializer.Deserialize<DepthMap>(stream, json.Options)
            ?? throw new InvalidDataException("Empty candidate depth map.");
    }

}
