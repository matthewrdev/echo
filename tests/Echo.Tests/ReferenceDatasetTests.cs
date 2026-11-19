using System.ComponentModel.Composition;
using System.Security.Cryptography;
using System.Text.Json;
using Echo.Projects;

namespace Echo.Tests;

[Export]
public sealed class ReferenceDatasetTests
{
    private readonly IProjectStoreFactory stores;

    [ImportingConstructor]
    public ReferenceDatasetTests(IProjectStoreFactory stores)
    {
        this.stores = stores;
    }

    internal void Run(TestSuite tests, string assetsRoot)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-datasets.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var datasets = JsonSerializer.Deserialize<List<ReferenceDataset>>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("Missing reference dataset catalogue.");

        tests.Run("Reference catalogue covers the source-backed regions and both Blue Lake areas", () =>
        {
            var regions = new[]
            {
                "siurana",
                "margalef",
                "blue-lake",
                "kalymnos",
                "rogers-canyon",
                "suesca",
                "urbenville",
                "eagle-rock",
                "valle-de-los-condores",
                "ceuse",
                "geyikbayiri",
                "bulahdelah"
            };
            Check(datasets.Select(dataset => dataset.Folder.Split('/')[0]).Distinct().Order().SequenceEqual(regions.Order()), $"Expected {regions.Length} distinct regions with original source photos");
            var blueLake = new[] { "blue-lake/elephant-buttress", "blue-lake/northern-buttress" };
            Check(datasets.Where(dataset => dataset.Folder.StartsWith("blue-lake/", StringComparison.Ordinal)).Select(dataset => dataset.Folder).Order().SequenceEqual(blueLake.Order()), "Expected both Blue Lake source captures");
            Check(datasets.Select(dataset => dataset.AreaId).Distinct().Count() == datasets.Count, "Duplicate Red-Point area");
            foreach (var dataset in datasets)
            {
                Check(Guid.TryParse(dataset.AreaId, out _), "Invalid Red-Point area ID");
                Check(Guid.TryParse(dataset.PolycamCaptureId, out _), "Missing original Polycam capture ID");
                Check(dataset.PhotoCount >= 3, "A reconstruction fixture needs source photos");
                Check(dataset.ModelUrl == $"https://redpointaus.blob.core.windows.net/assets/{dataset.AreaId}/{dataset.ModelFile}", "Reference model must belong to the recorded area");
                Check(string.IsNullOrEmpty(dataset.ReferenceSceneFile) == string.IsNullOrEmpty(dataset.ReferenceSceneSha256), "Reference scene path and checksum must be recorded together");
            }
        });

        foreach (var dataset in datasets)
        {
            tests.Run($"{dataset.Name}: complete original photo set and exact Red-Point model", () => ValidateFiles(assetsRoot, dataset, options));
            tests.Run($"{dataset.Name}: Echo imports representative original photographs", () => ImportPhotos(assetsRoot, dataset, options));
        }

        tests.Run("Blue Lake: both captures merge while retaining originals and avoiding repeat imports", () => ImportBlueLake(assetsRoot, datasets, options));
    }

    private void ImportBlueLake(string root, List<ReferenceDataset> datasets, JsonSerializerOptions options)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "echo-blue-lake-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = stores.Open(Path.Combine(temporary, "combined.echo"));
            store.Init("Blue Lake combined");
            var expected = new Dictionary<string, ReferencePhoto>(StringComparer.Ordinal);
            foreach (var dataset in datasets.Where(dataset => dataset.Folder.StartsWith("blue-lake/", StringComparison.Ordinal)))
            {
                var directory = SafePath(root, dataset.Folder);
                var manifest = ReadPhotos(directory, options);
                var selected = new[] { manifest.Photos[0], manifest.Photos[manifest.Photos.Count / 2], manifest.Photos[^1] };
                var inputs = Path.Combine(temporary, dataset.AreaId);
                Directory.CreateDirectory(inputs);
                for (var index = 0; index < selected.Length; index++)
                {
                    var photo = selected[index];
                    var source = SafePath(Path.Combine(directory, "photos"), photo.File);
                    File.Copy(source, Path.Combine(inputs, index + Path.GetExtension(source)));
                    expected[photo.Sha256] = photo;
                }

                var imported = store.Import(inputs, dataset.Folder, int.MaxValue);
                Check(imported.All(photo => photo.Sets.Contains(dataset.Folder)), "Capture provenance must survive import");
                store.Import(inputs, dataset.Folder, int.MaxValue);
            }

            var project = store.Load();
            Check(project.Images.Count == expected.Count, "Repeated imports must not duplicate photographs");
            Check(project.Images.Select(photo => photo.Hash).ToHashSet().SetEquals(expected.Keys), "Combined project must contain both capture sets");
            foreach (var photo in project.Images)
            {
                var original = expected[photo.Hash];
                var retained = store.PathFor($"sources/{photo.Id}.original{Path.GetExtension(original.File).ToLowerInvariant()}");
                Check(Hash(retained) == original.Sha256, "Merging captures must preserve original photo bytes");
            }
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
        }
    }

    private void ValidateFiles(string root, ReferenceDataset dataset, JsonSerializerOptions options)
    {
        var directory = SafePath(root, dataset.Folder);
        var model = SafePath(directory, dataset.ModelFile);
        Check(new FileInfo(model).Length == dataset.ModelBytes, "Model size differs from the published reference");
        Check(Hash(model) == dataset.ModelSha256, "Model checksum differs from the published reference");
        using (var stream = File.OpenRead(model))
        using (var reader = new BinaryReader(stream))
        {
            Check(reader.ReadUInt32() == 0x46546c67 && reader.ReadUInt32() == 2, "Expected a GLB version 2 model");
            Check(reader.ReadUInt32() == stream.Length, "Truncated GLB reference");
        }

        if (!string.IsNullOrEmpty(dataset.ReferenceSceneFile))
        {
            var scenePath = SafePath(directory, dataset.ReferenceSceneFile);
            Check(Hash(scenePath) == dataset.ReferenceSceneSha256, "Red-Point reference scene changed");
            using var scene = JsonDocument.Parse(File.ReadAllText(scenePath));
            Check(scene.RootElement.GetProperty("AreaId").GetString() == dataset.AreaId, "Reference scene belongs to a different area");
        }

        var manifestPath = Path.Combine(directory, "source-photos.json");
        Check(Hash(manifestPath) == dataset.PhotoManifestSha256, "Source photo inventory changed");
        var manifest = ReadPhotos(directory, options);
        Check(manifest.PolycamCaptureId == dataset.PolycamCaptureId, "Photos belong to a different Polycam capture");
        Check(manifest.Photos.Count == dataset.PhotoCount, "Incomplete source photo inventory");
        Check(manifest.Photos.Select(photo => photo.File).Distinct().Count() == dataset.PhotoCount, "Duplicate source paths");
        var photosDirectory = Path.Combine(directory, "photos");
        var actual = Directory.EnumerateFiles(photosDirectory, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) != ".DS_Store")
            .Select(file => Path.GetRelativePath(photosDirectory, file).Replace('\\', '/')).Order().ToArray();
        Check(actual.SequenceEqual(manifest.Photos.Select(photo => photo.File).Order()), "Missing or unexpected source photos");
        foreach (var photo in manifest.Photos)
        {
            var file = SafePath(photosDirectory, photo.File);
            Check(new FileInfo(file).Length == photo.Bytes && Hash(file) == photo.Sha256, $"Original photo changed: {photo.File}");
        }
    }

    private void ImportPhotos(string root, ReferenceDataset dataset, JsonSerializerOptions options)
    {
        var directory = SafePath(root, dataset.Folder);
        var manifest = ReadPhotos(directory, options);
        Check(manifest.Photos.Count >= 3, "Need at least three original photos");
        var selected = new[] { manifest.Photos[0], manifest.Photos[manifest.Photos.Count / 2], manifest.Photos[^1] };
        var temporary = Path.Combine(Path.GetTempPath(), "echo-reference-" + Guid.NewGuid().ToString("N"));
        try
        {
            var inputs = Path.Combine(temporary, "inputs");
            Directory.CreateDirectory(inputs);
            for (var index = 0; index < selected.Length; index++)
            {
                var photo = selected[index];
                var source = SafePath(Path.Combine(directory, "photos"), photo.File);
                File.Copy(source, Path.Combine(inputs, index + Path.GetExtension(source)));
            }
            var store = stores.Open(Path.Combine(temporary, "fixture.echo"));
            store.Init(dataset.Name);
            var imported = store.Import(inputs, dataset.Folder, int.MaxValue);
            Check(imported.Count == selected.Select(photo => photo.Sha256).Distinct().Count(), "Echo skipped an unsupported source photo");
            foreach (var photo in imported)
            {
                Check(photo.Width > 0 && photo.Height > 0, "Invalid imported image dimensions");
                Check(photo.FocalPixels > 0 && double.IsFinite(photo.FocalPixels), "Invalid initial camera focal length");
                var original = selected.FirstOrDefault(candidate => candidate.Sha256 == photo.Hash)
                    ?? throw new InvalidDataException("Imported hash must identify an original source photo");
                var retained = store.PathFor($"sources/{photo.Id}.original{Path.GetExtension(original.File).ToLowerInvariant()}");
                Check(Hash(retained) == original.Sha256, "Echo must retain the exact original photograph");
                Check(File.Exists(store.PathFor(photo.Source)), "Missing imported image");
            }
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
        }
    }

    private ReferencePhotoManifest ReadPhotos(string directory, JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize<ReferencePhotoManifest>(File.ReadAllText(Path.Combine(directory, "source-photos.json")), options)
            ?? throw new InvalidDataException("Missing source photo inventory.");
    }

    private string SafePath(string root, string relative)
    {
        var directory = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(directory))
        {
            directory += Path.DirectorySeparatorChar;
        }
        var path = Path.GetFullPath(Path.Combine(root, relative));
        Check(path.StartsWith(directory, StringComparison.Ordinal), "Reference path escapes its dataset");
        return path;
    }

    private string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
