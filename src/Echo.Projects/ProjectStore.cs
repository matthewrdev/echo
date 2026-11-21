using System.Text.Json;
using Echo.Models;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Metadata;
using SkiaSharp;

namespace Echo.Projects;

public sealed class ProjectStore : IProjectStore
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IMetadataReader metadataReader;
    private readonly IRasterImageService rasterImageService;

    public ProjectStore(string root,
        IFileOperations fileOperations,
        IJsonCodec jsonCodec,
        IMetadataReader metadataReader,
        IRasterImageService rasterImageService)
    {
        Root = Path.GetFullPath(root);
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.metadataReader = metadataReader;
        this.rasterImageService = rasterImageService;
    }

    public string Root
    {
        get;
    }

    public string PathFor(string relative) => Path.Combine(Root, relative);

    public ProjectData Load()
    {
        if (!File.Exists(PathFor("project.json")))
        {
            throw new EchoException("Not an Echo project. Run project init first.");
        }

        var data = jsonCodec.Read<ProjectData>(PathFor("project.json"));
        if (data.SchemaVersion != 1)
        {
            throw new EchoException("Unsupported project schema.");
        }

        return data;
    }

    public void Init(string? name = null)
    {
        Directory.CreateDirectory(Root);
        using var gate = Lock("edit");
        if (File.Exists(PathFor("project.json")))
        {
            throw new EchoException("Project already exists.");
        }

        foreach (var folder in new[]
        {
            "sources",
            "previews",
            "artifacts",
            "runs",
            "revisions"
        }
        )
        {
            Directory.CreateDirectory(PathFor(folder));
        }

        fileOperations.AtomicJson(PathFor("project.json"), new ProjectData { Name = name ?? Path.GetFileName(Root) });
    }

    public FileStream Lock(string name)
    {
        try
        {
            return new FileStream(PathFor($".{name}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new EchoException($"Project {name} is busy. Retry after the other operation finishes.", 6);
        }
    }

    public ProjectData Edit(Action<ProjectData> action)
    {
        using var gate = Lock("edit");
        var data = Load();
        action(data);
        data.Revision++;
        SaveRevision(data);
        fileOperations.AtomicJson(PathFor("project.json"), data);
        return data;
    }

    public List<Photo> Import(string directory, string set, int limit, Action<object>? progress = null)
    {
        using var importLease = Lock("import");
        if (!Directory.Exists(directory))
        {
            throw new EchoException("Image directory does not exist.");
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".tif",
            ".tiff",
            ".heic",
            ".heif"
        };
        var paths = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(p => extensions.Contains(Path.GetExtension(p))).Order(StringComparer.Ordinal).Take(limit).ToArray();
        if (paths.Length == 0)
        {
            throw new EchoException("No supported photos found.");
        }

        var added = new List<Photo>();
        var knownPhotos = Load().Images.DistinctBy(photo => photo.Hash).ToDictionary(photo => photo.Hash, photo => photo.Id);
        ProjectData? lastCheckpoint = null;
        var skipped = 0;
        try
        {
            progress?.Invoke(new PhotoImportProgress("imageImportStarted", 0, 0, paths.Length, 0));
            for (var index = 0; index < paths.Length; index++)
            {
                var path = paths[index];
                var hash = fileOperations.HashFile(path);
                if (knownPhotos.TryGetValue(hash, out var duplicateId))
                {
                    lastCheckpoint = CheckpointImport(project =>
                    {
                        var photo = project.Images.Single(photo => photo.Id == duplicateId);
                        if (photo.Sets.Contains(set))
                        {
                            return false;
                        }

                        photo.Sets.Add(set);
                        return true;
                    }) ?? lastCheckpoint;
                    skipped++;
                    progress?.Invoke(new PhotoImportProgress("imageImportSkipped", added.Count, index + 1, paths.Length,
                        skipped, duplicateId, Path.GetFileName(path)));
                    continue;
                }

                var image = ImportPhoto(path, hash, set);
                lastCheckpoint = CheckpointImport(project =>
                {
                    project.Images.Add(image);
                    return true;
                });
                knownPhotos.Add(hash, image.Id);
                added.Add(image);
                progress?.Invoke(new PhotoImportProgress("imageImported", added.Count, index + 1, paths.Length,
                    skipped, image.Id, image.Name));
            }
        }
        finally
        {
            if (lastCheckpoint != null)
            {
                SaveRevision(lastCheckpoint);
            }
        }

        return added;
    }

    private ProjectData? CheckpointImport(Func<ProjectData, bool> apply)
    {
        using var gate = Lock("edit");
        var data = Load();
        if (!apply(data))
        {
            return null;
        }

        // Keep committed photos restartable without archiving a copy of the full project for each one.
        data.Revision++;
        fileOperations.AtomicJson(PathFor("project.json"), data);
        return data;
    }

    private void SaveRevision(ProjectData data)
    {
        fileOperations.AtomicJson(PathFor($"revisions/{data.Revision:D6}.json"), data);
    }

    private Photo ImportPhoto(string path, string hash, string set)
    {
        var metadata = metadataReader.Read(path);
        using var bitmap = rasterImageService.Decode(path);
        var image = new Photo
        {
            Id = hash[..20],
            Hash = hash,
            Name = Path.GetFileName(path),
            Width = bitmap.Width,
            Height = bitmap.Height,
            Source = $"sources/{hash[..20]}.jpg",
            Preview = $"previews/{hash[..20]}.jpg",
            Metadata = metadata,
            Sets = [set],
            Camera = Text(metadata, "Model") ?? "Unknown camera",
            CapturedAt = Text(metadata, "DateTimeOriginal") ?? ""
        };
        var focal35 = Number(metadata, "FocalLengthIn35mmFormat");
        image.FocalPixels = focal35 is > 0 ? Math.Max(image.Width, image.Height) * focal35.Value / 36 : image.Width * .8660254;
        if (focal35 is > 0)
        {
            image.FocalSource = "EXIF 35mm equivalent (initial estimate; no distortion calibration)";
        }

        var lat = Number(metadata, "GPSLatitude");
        var lon = Number(metadata, "GPSLongitude");
        if (lat.HasValue && lon.HasValue)
        {
            image.Gps = new GeoPosition
            {
                Latitude = lat.Value,
                Longitude = lon.Value,
                Altitude = Number(metadata, "AbsoluteAltitude") ?? Number(metadata, "GPSAltitude")
            };
            image.Gps.AltitudeDatum = AltitudeReferenceResolver.Resolve(image.Gps, metadata).Datum;
        }

        var yaw = Number(metadata, "GimbalYawDegree");
        var pitch = Number(metadata, "GimbalPitchDegree");
        if (yaw.HasValue && pitch.HasValue)
        {
            image.Attitude = new Attitude
            {
                Yaw = yaw.Value,
                Pitch = pitch.Value,
                Roll = Number(metadata, "GimbalRollDegree") ?? 0
            };
        }
        else
        {
            image.Attitude = IPhoneCapturePose.Estimate(metadata);
        }

        var sidecar = path + ".echo.json";
        if (File.Exists(sidecar))
        {
            var supplied = jsonCodec.Read<Photo>(sidecar);
            image.Gps = supplied.Gps ?? image.Gps;
            image.Attitude = supplied.Attitude ?? image.Attitude;
            if (supplied.FocalPixels > 0)
            {
                image.FocalPixels = supplied.FocalPixels;
                image.FocalSource = "explicit sidecar";
            }
        }

        image.Gps?.Validate();
        rasterImageService.Save(bitmap, PathFor(image.Source), SKEncodedImageFormat.Jpeg);
        image.SourceHash = fileOperations.HashFile(PathFor(image.Source));
        using var thumb = rasterImageService.Resize(bitmap, 480);
        rasterImageService.Save(thumb, PathFor(image.Preview), SKEncodedImageFormat.Jpeg);
        // Each imported photo is a complete, restartable unit. Original bytes are retained separately.
        var original = PathFor($"sources/{image.Id}.original{Path.GetExtension(path).ToLowerInvariant()}");
        File.Copy(path, original, true);
        return image;
    }

    public void AddHints(List<Hint> hints)
    {
        Edit(p =>
        {
            foreach (var hint in hints)
            {
                if (!p.Images.Any(i => i.Id == hint.ImageId))
                {
                    throw new EchoException("Hint references an unknown image.", 2);
                }

                if (hint.Type is not ("camera" or "overlap" or "tie"))
                {
                    throw new EchoException("v0.1 hint types: camera, overlap, tie.", 2);
                }

                hint.Position?.Validate();
                if (hint.Attitude != null && !new[]
                {
                    hint.Attitude.Yaw,
                    hint.Attitude.Pitch,
                    hint.Attitude.Roll
                }.All(double.IsFinite))
                {
                    throw new EchoException("Non-finite attitude.", 2);
                }

                var emptyCameraHint = hint.Type == "camera"
                    && hint.Position == null
                    && hint.Attitude == null;
                if (emptyCameraHint)
                {
                    throw new EchoException("Camera hint needs position or attitude.", 2);
                }

                if (hint.Type is "overlap" or "tie")
                {
                    if (hint.OtherImageId == hint.ImageId || !p.Images.Any(i => i.Id == hint.OtherImageId))
                    {
                        throw new EchoException("Hint needs a different second image.", 2);
                    }

                    if (hint.Type == "tie")
                    {
                        ValidatePixel(hint.Pixel, p.Images.Single(i => i.Id == hint.ImageId));
                        ValidatePixel(hint.OtherPixel, p.Images.Single(i => i.Id == hint.OtherImageId));
                    }
                }

                p.Hints.RemoveAll(h => h.Id == hint.Id);
                p.Hints.Add(hint);
            }
        });
    }

    private void ValidatePixel(double[]? pixel, Photo photo)
    {
        if (pixel is not { Length: 2 } || !pixel.All(double.IsFinite))
        {
            throw new EchoException("Tie points use pixels in the normalised full-resolution image.", 2);
        }

        var outsideImage = pixel[0] < 0
            || pixel[1] < 0
            || pixel[0] >= photo.Width
            || pixel[1] >= photo.Height;
        if (outsideImage)
        {
            throw new EchoException("Tie points use pixels in the normalised full-resolution image.", 2);
        }
    }

    private string? Text(Dictionary<string, JsonElement> data, string key) => data.TryGetValue(key, out var v) ? v.ToString() : null;

    private double? Number(Dictionary<string, JsonElement> data, string key)
    {
        if (!data.TryGetValue(key, out var value))
        {
            return null;
        }

        var parsed = double.TryParse(value.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var number);
        return parsed && double.IsFinite(number) ? number : null;
    }

    public RunData GetRun(string id = "latest")
    {
        if (id == "latest")
        {
            var file = Directory.GetFiles(PathFor("runs"), "run.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            return file == null ? throw new EchoException("No runs yet.") : ReadRun(file);
        }

        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new EchoException("Invalid run ID.", 2);
        }

        return ReadRun(PathFor($"runs/{id}/run.json"));
    }

    private RunData ReadRun(string path)
    {
        var run = jsonCodec.Read<RunData>(path);
        if (run.Status == "running")
        {
            try
            {
                var progress = jsonCodec.Read<RunProgress>(Path.Combine(Path.GetDirectoryName(path)!, "progress.json"));
                if (run.Progress == null || progress.Updated >= run.Progress.Updated)
                {
                    run.Progress = progress;
                }
            }
            catch (FileNotFoundException)
            {
                // Older workers use run.json alone. A checkpoint may also
                // atomically publish the full state and remove this sidecar.
            }
        }

        return run;
    }
}
