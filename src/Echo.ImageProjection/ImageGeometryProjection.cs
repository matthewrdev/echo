using Echo.Geometry;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Meshing;
using Echo.Models;
using Echo.Projects;
using Echo.Texturing;

namespace Echo.ImageProjection;

internal sealed class ImageGeometryProjection : IImageGeometryProjection
{
    private readonly IProjectStoreFactory projects;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IRasterImageService raster;
    private readonly IMeshRaycasterFactory raycasters;
    private readonly IExternalImageRegistration registration;

    public ImageGeometryProjection(IProjectStoreFactory projects, IJsonCodec json, IFileOperations files,
        IRasterImageService raster, IMeshRaycasterFactory raycasters, IExternalImageRegistration registration)
    {
        this.projects = projects;
        this.json = json;
        this.files = files;
        this.raster = raster;
        this.raycasters = raycasters;
        this.registration = registration;
    }

    public ImageGeometryResult Project(ImageGeometryRequest request, CancellationToken cancellation)
    {
        ValidateRequest(request);
        var store = projects.Open(request.ProjectPath);
        var run = store.GetRun(request.RunId);
        if (!run.Artifacts.TryGetValue("alignment", out var alignment) || !run.Artifacts.TryGetValue("mesh", out var mesh))
        {
            throw new EchoException("The selected run needs a saved alignment and mesh.", 2);
        }

        var scene = json.Read<ReconstructionOverview>(store.PathFor(alignment + "/scene.json"));
        var photo = ResolvePhoto(request, run, scene);
        ValidateDocument(request.Document, photo);
        var meshPath = ResolveMesh(store, run, mesh);
        var surface = json.Read<Surface>(store.PathFor(meshPath));
        if (surface.Faces.Count == 0)
        {
            throw new EchoException("The selected run has an empty mesh.", 2);
        }

        using var raycaster = raycasters.Create(surface, cancellation);
        var resolved = ResolveCamera(request, store, run, scene, photo, raycaster, cancellation);
        var projected = ImageGeometrySampler.Project(request, photo, resolved, raycaster, cancellation);
        var reviews = ResolveReviews(request, run, scene, projected, raycaster, cancellation);
        var file = run.Artifacts.TryGetValue("export", out var export) ? store.PathFor(export + "/cliff.glb") : null;
        var checksum = File.Exists(file) ? files.HashFile(file!) : null;
        return new ImageGeometryResult("echo-image-geometry", 1, run.Id, scene.Frame, meshPath, checksum,
            request.ImageId ?? Path.GetFileName(photo.Source), photo.Width, photo.Height, resolved.Fit, projected, reviews);
    }

    private List<ImageProjectionReview> ResolveReviews(ImageGeometryRequest request, RunData run,
        ReconstructionOverview scene, IReadOnlyList<ProjectedImageFeature> features, IMeshRaycaster mesh,
        CancellationToken cancellation)
    {
        var reviews = new List<ImageProjectionReview>();
        foreach (var imageId in request.ReviewImageIds.Distinct(StringComparer.Ordinal))
        {
            var image = run.Snapshot.Images.Find(candidate => candidate.Id == imageId);
            var pose = scene.Cameras.Find(candidate => candidate.ImageId == imageId);
            if (image == null || pose == null)
            {
                throw new EchoException($"Review image '{imageId}' has no saved camera pose in the selected run.", 2);
            }

            reviews.Add(ImageProjectionReviewBuilder.Build(image, pose, features, mesh, cancellation));
        }

        return reviews;
    }

    private void ValidateRequest(ImageGeometryRequest request)
    {
        if ((request.ImageId == null) == (request.ImagePath == null))
        {
            throw new EchoException("Provide either --image FILE or --image-id ID.", 2);
        }

        var invalidSampling = !double.IsFinite(request.SampleStep) || request.SampleStep <= 0
            || !ValidPositive(request.MaximumJump);
        if (invalidSampling || !ValidPositive(request.FocalPixels))
        {
            throw new EchoException("Sampling and camera options must be finite positive numbers.", 2);
        }

        var hasControls = request.Document.ControlPoints is { Count: > 0 };
        var usesRegistrationOptions = hasControls || request.ReferenceImageIds.Count > 0 || request.FocalPixels != null;
        if (request.ImageId != null && usesRegistrationOptions)
        {
            throw new EchoException("Camera registration options apply to external images only.", 2);
        }

        if (hasControls && request.ReferenceImageIds.Count > 0)
        {
            throw new EchoException("Choose control points or reference image IDs to register the external image.", 2);
        }
    }

    private bool ValidPositive(double? value) => value == null || double.IsFinite(value.Value) && value > 0;

    private Photo ResolvePhoto(ImageGeometryRequest request, RunData run, ReconstructionOverview scene)
    {
        if (request.ImageId is { } imageId)
        {
            var photo = run.Snapshot.Images.Find(candidate => candidate.Id == imageId);
            if (photo == null || !scene.Cameras.Any(camera => camera.ImageId == imageId))
            {
                throw new EchoException($"Image '{imageId}' has no saved camera pose in the selected run.", 2);
            }

            return photo;
        }

        var path = Path.GetFullPath(request.ImagePath!);
        using var image = raster.Decode(path);
        return new Photo
        {
            Id = "external-image",
            Source = path,
            Width = image.Width,
            Height = image.Height
        };
    }

    private void ValidateDocument(ImageGeometryDocument document, Photo photo)
    {
        if (document.Features is not { Count: > 0 })
        {
            throw new EchoException("The geometry document needs at least one feature.", 2);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in document.Features)
        {
            if (feature == null || string.IsNullOrWhiteSpace(feature.Id) || !ids.Add(feature.Id))
            {
                throw new EchoException("Each geometry feature needs a distinct non-empty ID.", 2);
            }

            var required = feature.Geometry switch
            {
                "point" => 1,
                "polyline" => 2,
                "polygon" => 3,
                _ => throw new EchoException($"Feature '{feature.Id}' needs geometry point, polyline or polygon.", 2)
            };
            var invalidPointCount = feature.Geometry == "point" && feature.Pixels?.Count != 1;
            if (feature.Pixels == null || invalidPointCount || feature.Pixels.Any(pixel => !ValidPixel(pixel, photo)))
            {
                throw new EchoException($"Feature '{feature.Id}' contains invalid pixels or an incorrect point count.", 2);
            }

            if (feature.Pixels.Select(pixel => new V3(pixel[0], pixel[1], 0)).Distinct().Count() < required)
            {
                throw new EchoException($"Feature '{feature.Id}' needs at least {required} distinct pixels.", 2);
            }
        }

        if (document.ControlPoints is { Count: > 0 } controls)
        {
            var validControls = controls.Count >= 6 && controls.All(control =>
                control != null && ValidPixel(control.Pixel, photo) && control.Position is { Length: 3 }
                && control.Position.All(double.IsFinite));
            if (!validControls)
            {
                throw new EchoException("Provide at least six valid control points with an image pixel and a model XYZ position.", 2);
            }
        }
    }

    private bool ValidPixel(double[]? pixel, Photo photo) =>
        pixel is { Length: 2 } && pixel[0] >= 0 && pixel[0] < photo.Width && pixel[1] >= 0 && pixel[1] < photo.Height;

    private string ResolveMesh(IProjectStore store, RunData run, string mesh)
    {
        if (run.Artifacts.TryGetValue("texture", out var texture))
        {
            var summary = json.Read<TextureResult>(store.PathFor(texture + "/texture.json"));
            if (summary.MeshFile is { } name)
            {
                if (string.IsNullOrEmpty(name) || name is "." or ".." || Path.GetFileName(name) != name)
                {
                    throw new EchoException("Invalid texture mesh artifact file name.", 4);
                }

                return texture + "/" + name;
            }
        }

        return mesh + "/mesh.json";
    }

    private RegistrationResult ResolveCamera(ImageGeometryRequest request, IProjectStore store, RunData run,
        ReconstructionOverview scene, Photo photo, IMeshRaycaster mesh, CancellationToken cancellation)
    {
        if (request.ImageId != null)
        {
            var pose = scene.Cameras.Single(camera => camera.ImageId == request.ImageId);
            var focal = CameraGeometry.Focal(photo, pose);
            if (!double.IsFinite(focal) || focal <= 0)
            {
                throw new EchoException("The saved camera has no usable focal length.", 2);
            }

            pose.FocalPixels = focal;
            return new RegistrationResult(new ImageCameraFit("capturePose", pose), []);
        }

        var external = new ExternalRegistrationRequest(store, run, scene, photo, photo.Source,
            request.Document.ControlPoints, request.ReferenceImageIds, request.FocalPixels, mesh);
        return registration.Register(external, cancellation);
    }
}
