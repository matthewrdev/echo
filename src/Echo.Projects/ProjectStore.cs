using System.Text.Json;
using Echo.Models;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Metadata;
using Echo.Storage.Journals;
using SkiaSharp;

namespace Echo.Projects;

public sealed class ProjectStore : IProjectStore
{
    private const string ImportJournalFile = "project.import.bin";

    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IMetadataReader metadataReader;
    private readonly IRasterImageService rasterImageService;
    private readonly ProjectSnapshotCodec snapshots = new();
    private readonly ProjectImportRecordCodec imports = new();
    private readonly BinaryJournalReader<ProjectImportRecord> importReader;
    private readonly RunProgressCodec progressSnapshots = new();

    public ProjectStore(string root,
        IFileOperations fileOperations,
        IJsonCodec jsonCodec,
        IMetadataReader metadataReader,
        IRasterImageService rasterImageService)
    {
        Root = Path.GetFullPath(root);
        importReader = new BinaryJournalReader<ProjectImportRecord>(imports);
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
        if (!File.Exists(PathFor("project.bin")))
        {
            if (File.Exists(PathFor("project.json")))
            {
                throw new EchoException("This project needs the packed storage migration. Run Echo.Migrate on the project folder.", 2);
            }

            throw new EchoException("Not an Echo project. Run project init first.");
        }

        using var journal = importReader.OpenRead(PathFor(ImportJournalFile));
        var data = snapshots.Read(PathFor("project.bin"));
        if (data.SchemaVersion != 1)
        {
            throw new EchoException("Unsupported project schema.");
        }

        if (journal != null)
        {
            ReplayImport(data, journal);
        }

        return data;
    }

    private void ReplayImport(ProjectData data, FileStream journal)
    {
        var photos = data.Images.ToDictionary(photo => photo.Id);
        foreach (var record in importReader.Read(journal))
        {
            if (record.Revision <= data.Revision)
            {
                continue;
            }

            if (record.Revision != data.Revision + 1)
            {
                throw new InvalidDataException("Project import journal revisions are out of order.");
            }

            if (record.Photo is { } photo)
            {
                photos.Add(photo.Id, photo);
                data.Images.Add(photo);
            }
            else if (record.PhotoId is { } id && record.Set is { } set && photos.TryGetValue(id, out var existing))
            {
                existing.Sets.Add(set);
            }
            else
            {
                throw new InvalidDataException("Invalid project import journal record.");
            }

            data.Revision = record.Revision;
        }
    }

    public void Init(string? name = null)
    {
        Directory.CreateDirectory(Root);
        using var gate = Lock("edit");
        if (File.Exists(PathFor("project.bin")) || File.Exists(PathFor("project.json")))
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

        var project = new ProjectData { Name = name ?? Path.GetFileName(Root) };
        fileOperations.AtomicWrite(PathFor("project.bin"), stream => snapshots.Write(stream, project));
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
        SaveProject(data);
        return data;
    }

    public List<Photo> Import(string directory, string set, int limit, Action<object>? progress = null)
    {
        using var importLease = Lock("import");
        if (!Directory.Exists(directory))
        {
            throw new EchoException("Image directory does not exist.");
        }

        var entries = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsSupportedPhoto)
            .Order(StringComparer.Ordinal)
            .Take(limit)
            .Select(path => new PhotoImportEntry(path, set))
            .ToArray();
        return ImportEntries(entries, progress);
    }

    public List<Photo> Import(IReadOnlyList<PhotoImportEntry> entries, Action<object>? progress = null)
    {
        using var importLease = Lock("import");
        foreach (var entry in entries)
        {
            var valid = entry != null && !string.IsNullOrWhiteSpace(entry.Set)
                && entry.Path != null && Path.IsPathFullyQualified(entry.Path) && IsSupportedPhoto(entry.Path);
            if (!valid || !File.Exists(entry?.Path))
            {
                throw new EchoException($"Invalid or missing photo in import manifest: {entry?.Path}", 2);
            }
        }

        return ImportEntries(entries, progress);
    }

    private bool IsSupportedPhoto(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".heic" or ".heif";
    }

    private List<Photo> ImportEntries(IReadOnlyList<PhotoImportEntry> entries, Action<object>? progress)
    {
        if (entries.Count == 0)
        {
            throw new EchoException("No supported photos found.");
        }

        using var gate = Lock("edit");
        var project = Load();
        var journalPath = PathFor(ImportJournalFile);
        var needsSnapshot = File.Exists(journalPath);
        var added = new List<Photo>();
        var knownPhotos = project.Images.DistinctBy(photo => photo.Hash).ToDictionary(photo => photo.Hash);
        var journal = new BinaryJournal<ProjectImportRecord>(journalPath, imports);
        var skipped = 0;
        var appendFailed = false;
        try
        {
            progress?.Invoke(new PhotoImportProgress("imageImportStarted", 0, 0, entries.Count, 0));
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var path = entry.Path;
                var set = entry.Set;
                var hash = fileOperations.HashFile(path);
                if (knownPhotos.TryGetValue(hash, out var existing))
                {
                    if (!existing.Sets.Contains(set))
                    {
                        Append(new ProjectImportRecord { Revision = project.Revision + 1, PhotoId = existing.Id, Set = set });
                        project.Revision++;
                        existing.Sets.Add(set);
                        needsSnapshot = true;
                    }

                    skipped++;
                    progress?.Invoke(new PhotoImportProgress("imageImportSkipped", added.Count, index + 1, entries.Count,
                        skipped, existing.Id, Path.GetFileName(path)));
                    continue;
                }

                var image = ImportPhoto(path, hash, set);
                Append(new ProjectImportRecord { Revision = project.Revision + 1, Photo = image });
                project.Revision++;
                project.Images.Add(image);
                knownPhotos.Add(hash, image);
                needsSnapshot = true;
                added.Add(image);
                progress?.Invoke(new PhotoImportProgress("imageImported", added.Count, index + 1, entries.Count,
                    skipped, image.Id, image.Name));
            }
        }
        finally
        {
            journal.Dispose();
            if (needsSnapshot && !appendFailed)
            {
                SaveProject(project);
            }
            else if (!appendFailed && new FileInfo(journalPath).Length == sizeof(uint))
            {
                File.Delete(journalPath);
            }
        }

        return added;

        void Append(ProjectImportRecord record)
        {
            try
            {
                journal.Append(record);
            }
            catch
            {
                appendFailed = true;
                throw;
            }
        }
    }

    private void SaveProject(ProjectData data)
    {
        fileOperations.AtomicWrite(PathFor($"revisions/{data.Revision:D6}.bin"), stream => snapshots.Write(stream, data));
        fileOperations.AtomicWrite(PathFor("project.bin"), stream => snapshots.Write(stream, data));
        File.Delete(PathFor(ImportJournalFile));
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
        var source = PathFor(image.Source);
        image.SourceHash = rasterImageService.Save(bitmap, source, SKEncodedImageFormat.Jpeg) ?? fileOperations.HashFile(source);
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

                var surveyPoint = hint.Type is "ground-control" or "survey-check";
                if (!surveyPoint && hint.Type is not ("camera" or "overlap" or "tie"))
                {
                    throw new EchoException("Hint types: camera, overlap, tie, ground-control, survey-check.", 2);
                }

                hint.Position?.Validate();
                if (surveyPoint && hint.Position?.Altitude == null)
                {
                    throw new EchoException("Survey points need a measured geographic position and ellipsoidal height.", 2);
                }
                if (hint.Attitude != null && !new[]
                {
                    hint.Attitude.Yaw,
                    hint.Attitude.Pitch,
                    hint.Attitude.Roll
                }.All(double.IsFinite))
                {
                    throw new EchoException("Non-finite attitude.", 2);
                }

                if (hint.VerticalUncertaintyMetres is { } vertical)
                {
                    var hasPosition = hint.Type == "camera" && hint.Position?.Altitude != null;
                    var hasAccuracy = double.IsFinite(vertical) && vertical > 0;
                    if (!hasPosition || !hasAccuracy)
                    {
                        throw new EchoException("Vertical uncertainty requires a camera position with an altitude and a positive uncertainty.", 2);
                    }
                }

                var emptyCameraHint = hint.Type == "camera"
                    && hint.Position == null
                    && hint.Attitude == null;
                if (emptyCameraHint)
                {
                    throw new EchoException("Camera hint needs position or attitude.", 2);
                }

                if (surveyPoint || hint.Type is "overlap" or "tie")
                {
                    if (hint.OtherImageId == hint.ImageId || !p.Images.Any(i => i.Id == hint.OtherImageId))
                    {
                        throw new EchoException("Hint needs a different second image.", 2);
                    }

                    if (surveyPoint || hint.Type == "tie")
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

    public IReadOnlyList<RunSummary> ListRuns()
    {
        var directory = PathFor("runs");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "run.json", SearchOption.AllDirectories)
            .Select(jsonCodec.Read<StoredRunState>)
            .OrderByDescending(run => run.Created)
            .Select(run => new RunSummary { Id = run.Id, Status = run.Status, Target = run.Target, Created = run.Created })
            .ToArray();
    }

    public void SaveRun(RunData run)
    {
        var directory = PathFor($"runs/{run.Id}");
        var snapshotPath = Path.Combine(directory, "snapshot.bin");
        if (!File.Exists(snapshotPath))
        {
            fileOperations.AtomicWrite(snapshotPath, stream => snapshots.Write(stream, run.Snapshot));
        }

        fileOperations.AtomicJson(Path.Combine(directory, "run.json"), new StoredRunState(run));
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
        var state = jsonCodec.Read<StoredRunState>(path);
        var directory = Path.GetDirectoryName(path)!;
        var run = state.WithSnapshot(snapshots.Read(Path.Combine(directory, "snapshot.bin")));
        if (run.Status == "running")
        {
            try
            {
                var progress = progressSnapshots.Read(Path.Combine(directory, "progress.bin"));
                if (run.Progress == null || progress.Updated >= run.Progress.Updated)
                {
                    run.Progress = progress;
                }
            }
            catch (FileNotFoundException)
            {
                // A run checkpoint includes progress and supersedes the sidecar.
            }
        }

        return run;
    }
}
