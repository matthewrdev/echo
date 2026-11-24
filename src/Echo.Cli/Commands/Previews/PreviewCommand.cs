using System.Globalization;
using Echo.Fusion;
using Echo.Imaging;
using Echo.Models;
using Echo.Pipeline.Previews;
using Echo.Projects;
using Echo.Rendering;
using Echo.Rendering.Previews;
using Echo.Storage.Artifacts;
using SkiaSharp;

namespace Echo.Cli.Commands.Previews;

public sealed class PreviewCommand : ICliCommand
{
    private const string ViewOptions = "--view front|back|left|right|top|oblique";

    private readonly IProjectStoreFactory stores;
    private readonly IStagePreviewArtifacts artifacts;
    private readonly IBulkArtifactStore bulk;
    private readonly IRasterImageService images;
    private readonly IModelRenderer models;
    private readonly IPreviewPngWriter png;
    private readonly ICommandOutput output;

    public PreviewCommand(IProjectStoreFactory stores, IStagePreviewArtifacts artifacts, IBulkArtifactStore bulk,
        IRasterImageService images, IModelRenderer models, IPreviewPngWriter png, ICommandOutput output)
    {
        this.stores = stores;
        this.artifacts = artifacts;
        this.bulk = bulk;
        this.images = images;
        this.models = models;
        this.png = png;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["preview"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("preview cameras",
            "--project PATH preview cameras STAGE [--run RUN_ID] [--camera IMAGE_ID]",
            "List stage photos, saved alignment poses, and match pairs for matching. Stages are features through export."),
        new("preview features", Syntax("features IMAGE_ID"),
            "Draw point, line and junction features over the source photograph."),
        new("preview matching", Syntax("matching IMAGE_ID IMAGE_ID", "--limit 250"),
            "Draw point and line correspondences across two photographs. The order controls the left and right panels."),
        new("preview alignment", Syntax("alignment", ViewOptions + " --cameras all|none|ID,ID | --camera IMAGE_ID"),
            "Draw sampled sparse points with camera frustums, or render from an aligned photo's pose."),
        new("preview depth", Syntax("depth IMAGE_ID", "--mode depth|confidence"),
            "Colour a saved per-photo depth or confidence map. Dark pixels mark missing depth. Default mode is depth."),
        new("preview cloud", Syntax("cloud", ViewOptions + " --cameras all|none|ID,ID | --camera IMAGE_ID"),
            "Draw sampled dense points, optionally with camera frustums, or render from an aligned photo's pose."),
        new("preview fusion", Syntax("fusion", "--view x|y|z --slice VOXEL_INDEX | --camera IMAGE_ID"),
            "Draw a signed-distance slice or project sampled near-surface voxels from an aligned photo's pose."),
        new("preview mesh", Syntax("mesh", ViewOptions + " | --camera IMAGE_ID", "--mode solid|wireframe|normals"),
            "Render the completed untextured mesh using macOS and Metal. Defaults to an oblique solid view."),
        new("preview texture", Syntax("texture", ViewOptions + " | --camera IMAGE_ID", "--mode textured|solid|wireframe|normals"),
            "Render the textured surface using macOS and Metal."),
        new("preview export", Syntax("export", ViewOptions + " | --camera IMAGE_ID", "--mode textured|solid|wireframe|normals"),
            "Render the saved GLB using macOS and Metal.")
    ];

    public int Execute(Arguments args)
    {
        var stage = args.Word(1, "");
        ValidateArguments(args, stage);
        var project = args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2);
        var store = stores.Open(project);
        store.Load();
        var run = store.GetRun(args.Get("run", "latest")!);
        var cameraId = args.Get("camera", null);
        if (stage == "cameras")
        {
            WriteCameras(store, run, args.RequiredWord(2), cameraId);
            return 0;
        }

        var camera = cameraId == null ? null : ResolveCamera(store, run, cameraId);
        var path = Path.GetFullPath(args.Get("output", null) ?? throw new EchoException("--output FILE.png is required.", 2));
        var width = args.Int("width", 1600);
        var defaultHeight = camera == null ? 1000 : (int)Math.Clamp(
            Math.Round((double)width * camera.PhotoHeight / camera.PhotoWidth), 1, 8192);
        var height = args.Int("height", defaultHeight);
        PreviewImage.ValidateOutput(path, width, height);
        if (stage is "mesh" or "texture" or "export")
        {
            RenderModel(store, run, stage, args, path, width, height, camera);
            return 0;
        }

        var area = new SKRect(0, 0, width, height);
        var selection = png.Write(path, width, height, canvas => stage switch
        {
            "features" => DrawFeatures(store, run, args.RequiredWord(2), canvas, area),
            "matching" => DrawMatching(store, run, args, canvas, area),
            "alignment" or "cloud" => DrawCloud(store, run, stage, args, canvas, width, height, camera),
            "depth" => DrawDepth(store, run, args, canvas, area),
            "fusion" => DrawFusion(store, run, args, canvas, area, width, height, camera),
            _ => throw new EchoException("Unknown preview stage.", 2)
        });
        output.Write(new
        {
            runId = run.Id,
            stage,
            output = path,
            width,
            height,
            selection
        });
        return 0;
    }

    private object DrawFeatures(IProjectStore store, RunData run, string imageId, SKCanvas canvas, SKRect area)
    {
        var photo = Photo(run, imageId);
        var features = artifacts.Features(store, run, imageId);
        using var bitmap = images.Decode(store.PathFor(photo.Source));
        PhotoOverlayPreview.Features(canvas, new PhotoPreview(photo, bitmap, area), features);
        return new
        {
            imageId,
            points = features.Features.Count,
            lines = features.Lines.Count
        };
    }

    private object DrawMatching(IProjectStore store, RunData run, Arguments args, SKCanvas canvas, SKRect area)
    {
        var first = Photo(run, args.RequiredWord(2));
        var second = Photo(run, args.RequiredWord(3));
        var limit = args.Int("limit", 250);
        var pair = artifacts.Matching(store, run, first.Id, second.Id);
        var reversed = pair.A != first.Id;
        var gap = Math.Min(12, area.Width / 12);
        var left = new SKRect(0, 0, area.MidX - gap / 2, area.Bottom);
        var right = new SKRect(area.MidX + gap / 2, 0, area.Right, area.Bottom);
        using var firstBitmap = images.Decode(store.PathFor(first.Source));
        using var secondBitmap = images.Decode(store.PathFor(second.Source));
        var firstPhoto = new PhotoPreview(first, firstBitmap, left);
        var secondPhoto = new PhotoPreview(second, secondBitmap, right);
        var drawn = PhotoOverlayPreview.Matching(canvas, reversed ? secondPhoto : firstPhoto,
            reversed ? firstPhoto : secondPhoto, pair, limit);
        return new
        {
            first = first.Id,
            second = second.Id,
            points = pair.Matches.Count,
            lines = pair.LineMatches.Count,
            drawn
        };
    }

    private object DrawCloud(IProjectStore store, RunData run, string stage, Arguments args, SKCanvas canvas,
        int width, int height, ModelCamera? camera)
    {
        var scene = stage == "alignment" ? artifacts.Alignment(store, run) : null;
        var cloud = scene == null ? artifacts.Cloud(store, run) : null;
        var points = scene?.Points ?? cloud!.Preview;
        if (camera != null)
        {
            var visible = CloudPreview.DrawFromCamera(canvas, width, height, points, camera);
            return new
            {
                camera = camera.ImageId,
                points = points.Count,
                visible
            };
        }

        var view = View(args);
        var selection = args.Get("cameras", scene == null ? "none" : "all")!;
        var poses = SelectFrustums(store, run, scene?.Cameras, cloud?.ImageIds, selection);
        CloudPreview.Draw(canvas, width, height, points, poses, view);
        return new
        {
            view,
            points = points.Count,
            retainedPoints = cloud?.RetainedSamples,
            cameras = poses.Count
        };
    }

    private IReadOnlyList<CameraPose> SelectFrustums(IProjectStore store, RunData run,
        IReadOnlyList<CameraPose>? available, IReadOnlyList<string>? cloudInputs, string selection)
    {
        if (selection == "none")
        {
            return [];
        }

        var poses = available ?? artifacts.Poses(store, run).Where(pose => cloudInputs!.Contains(pose.ImageId)).ToArray();
        if (selection == "all")
        {
            return poses;
        }

        var ids = selection.Split(',', StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal);
        return ids.Select(id => poses.FirstOrDefault(pose => pose.ImageId == id)
            ?? throw new EchoException($"Camera {id} is absent from this stage’s frustum overlay.", 2)).ToArray();
    }

    private object DrawDepth(IProjectStore store, RunData run, Arguments args, SKCanvas canvas, SKRect area)
    {
        var imageId = args.RequiredWord(2);
        var mode = args.Get("mode", "depth")!;
        if (mode is not ("depth" or "confidence"))
        {
            throw new EchoException("Depth mode must be depth or confidence.", 2);
        }

        using var data = bulk.OpenSession(64L * 1024 * 1024, 0);
        var map = data.Read<DepthMap>(artifacts.DepthPath(store, run, imageId));
        DepthPreview.Draw(canvas, area, map, mode == "confidence");
        return new
        {
            imageId,
            mode,
            sourceWidth = map.Width,
            sourceHeight = map.Height
        };
    }

    private object DrawFusion(IProjectStore store, RunData run, Arguments args, SKCanvas canvas, SKRect area,
        int width, int height, ModelCamera? camera)
    {
        var axis = args.Get("view", "z")!;
        if (axis is not ("x" or "y" or "z"))
        {
            throw new EchoException("Fusion view must be x, y or z.", 2);
        }

        var coordinate = args.Get("slice", null);
        int? slice = coordinate == null ? null : int.Parse(coordinate, CultureInfo.InvariantCulture);
        using var data = bulk.OpenSession(64L * 1024 * 1024, 0);
        var volume = data.Read<FusedVolume>(artifacts.FusionPath(store, run));
        if (camera != null)
        {
            var points = FusionPreviewSelection.Sample(volume.Samples)
                .Where(voxel => Math.Abs(voxel.Distance) <= volume.VoxelSize)
                .Select(voxel => new CloudPoint
                {
                    Position = [voxel.X * volume.VoxelSize, voxel.Y * volume.VoxelSize, voxel.Z * volume.VoxelSize]
                }).ToArray();
            var visible = CloudPreview.DrawFromCamera(canvas, width, height, points, camera);
            return new
            {
                camera = camera.ImageId,
                points = points.Length,
                visible,
                volume.VoxelSize
            };
        }

        var selected = FusionSlicePreview.Draw(canvas, area, volume, axis, slice);
        return new
        {
            axis,
            slice = selected,
            volume.VoxelSize
        };
    }

    private void RenderModel(IProjectStore store, RunData run, string stage, Arguments args, string path, int width,
        int height, ModelCamera? camera)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        string? temporary = null;
        try
        {
            var raw = stage == "mesh";
            string source;
            if (stage == "texture")
            {
                temporary = Path.Combine(Path.GetTempPath(), "echo-texture-preview-" + Guid.NewGuid().ToString("N"));
                source = Path.Combine(temporary, "cliff.glb");
                artifacts.WriteTextureModel(store, run, source);
            }
            else
            {
                source = raw ? artifacts.MeshPath(store, run) : artifacts.ExportPath(store, run);
            }

            var request = new ModelRenderRequest(source, path, width, height,
                View(args), args.Get("mode", raw ? "solid" : "textured")!)
            {
                RawMesh = raw,
                Camera = camera
            };
            var result = models.Render(request, cancellation.Token);
            output.Write(new
            {
                runId = run.Id,
                stage,
                camera = camera?.ImageId,
                result.Output,
                result.Width,
                result.Height,
                view = camera == null ? result.View : null,
                result.Mode,
                result.Vertices,
                result.Triangles
            });
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (temporary != null && Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private void WriteCameras(IProjectStore store, RunData run, string stage, string? cameraId)
    {
        if (stage is not ("features" or "matching" or "alignment" or "depth" or "cloud" or "fusion" or "mesh" or "texture" or "export"))
        {
            throw new EchoException("Use preview cameras --help for supported stages.", 2);
        }

        var inputs = artifacts.CameraInputs(store, run, stage);
        if (cameraId != null && !inputs.ImageIds.Contains(cameraId))
        {
            throw new EchoException($"Camera {cameraId} is absent from the saved {stage} camera list.", 2);
        }

        var photos = run.Snapshot.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var poses = artifacts.Poses(store, run).ToDictionary(pose => pose.ImageId, StringComparer.Ordinal);
        var cameras = inputs.ImageIds.Where(id => cameraId == null || id == cameraId).Select(id =>
        {
            var photo = photos.GetValueOrDefault(id);
            return new
            {
                imageId = id,
                photo?.Name,
                cameraModel = photo?.Camera,
                pose = poses.GetValueOrDefault(id)
            };
        });
        var pairs = inputs.Pairs?.Where(pair => cameraId == null || pair.A == cameraId || pair.B == cameraId)
            .Select(pair => new { pair.A, pair.B, pair.PointCount, pair.LineCount });
        output.Write(new
        {
            runId = run.Id,
            stage,
            inputs.Basis,
            cameras,
            pairs
        });
    }

    private ModelCamera ResolveCamera(IProjectStore store, RunData run, string imageId)
    {
        var photo = Photo(run, imageId);
        var pose = artifacts.Poses(store, run).FirstOrDefault(candidate => candidate.ImageId == imageId)
            ?? throw new EchoException($"Camera {imageId} has no aligned pose in run {run.Id}.", 2);
        var focal = pose.FocalPixels > 0 ? pose.FocalPixels : photo.FocalPixels;
        var camera = new ModelCamera(imageId, pose.Centre, pose.Rotation, focal, photo.Width, photo.Height);
        camera.Validate();
        return camera;
    }

    private void ValidateArguments(Arguments args, string stage)
    {
        var words = stage switch
        {
            "features" or "depth" or "cameras" => 3,
            "matching" => 4,
            "alignment" or "cloud" or "fusion" or "mesh" or "texture" or "export" => 2,
            _ => 0
        };
        if (args.Words.Count != words)
        {
            throw new EchoException("Use preview --help for stages and required image IDs.", 2);
        }

        foreach (var option in args.Options.Keys)
        {
            var allowed = option switch
            {
                "project" or "run" or "json" => true,
                "output" or "width" or "height" => stage != "cameras",
                "view" => stage is "alignment" or "cloud" or "fusion" or "mesh" or "texture" or "export",
                "camera" => stage is "cameras" or "alignment" or "cloud" or "fusion" or "mesh" or "texture" or "export",
                "cameras" => stage is "alignment" or "cloud",
                "mode" => stage is "depth" or "mesh" or "texture" or "export",
                "limit" => stage == "matching",
                "slice" => stage == "fusion",
                _ => false
            };
            if (!allowed)
            {
                throw new EchoException($"--{option} is not supported for preview {stage}.", 2);
            }
        }

        var presetView = args.Options.Keys.Any(option => option is "view" or "cameras" or "slice");
        if (args.Options.ContainsKey("camera") && presetView)
        {
            throw new EchoException("Use --camera by itself or choose a preset view, camera overlay or slice.", 2);
        }
    }

    private string View(Arguments args) => args.Get("view", "oblique")!;

    private Photo Photo(RunData run, string imageId)
    {
        return run.Snapshot.Images.FirstOrDefault(photo => photo.Id == imageId)
            ?? throw new EchoException($"Image {imageId} is not in run {run.Id}.", 2);
    }

    private string Syntax(string stage, string? options = null, string? mode = null)
    {
        var syntax = $"--project PATH preview {stage} --output NEW_FILE.png [--run RUN_ID] [--width PIXELS] [--height PIXELS]";
        if (options != null)
        {
            syntax += $" [{options}]";
        }

        return mode == null ? syntax : syntax + $" [{mode}]";
    }
}
