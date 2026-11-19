using System.ComponentModel.Composition;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Validation;

[Export(typeof(IValidationInputs))]
public sealed class ValidationInputs : IValidationInputs
{
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IProjectStoreFactory stores;

    [ImportingConstructor]
    public ValidationInputs(IJsonCodec json, IFileOperations files, IProjectStoreFactory stores)
    {
        this.json = json;
        this.files = files;
        this.stores = stores;
    }

    public IProjectStore Prepare(ReconstructionCase fixture, string assets, string output, CancellationToken cancellation)
    {
        var dataset = ValidationPaths.ContainedPath(assets, fixture.Dataset);
        var manifestPath = Path.Combine(dataset, "source-photos.json");
        if (files.HashFile(manifestPath) != fixture.PhotoManifestSha256)
        {
            throw new InvalidDataException("Source photo manifest changed; review the fixture before accepting new inputs.");
        }
        var manifest = json.Read<ValidationPhotoManifest>(manifestPath);
        var selected = fixture.PhotoHashes.ToHashSet(StringComparer.Ordinal);
        if (selected.Count < 3 || selected.Count != fixture.PhotoHashes.Count)
        {
            throw new InvalidDataException("A fixture requires at least three distinct original photo hashes.");
        }
        var photos = manifest.Photos.Where(photo => selected.Contains(photo.Sha256)).ToArray();
        if (photos.Length != selected.Count || photos.Select(photo => photo.Sha256).Distinct().Count() != selected.Count)
        {
            throw new InvalidDataException("The manifest does not contain exactly one original for every selected hash.");
        }

        // Stage only the pinned originals; preserve relative names and import ordering.
        var staging = Path.Combine(output, "inputs");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var photo in photos)
            {
                cancellation.ThrowIfCancellationRequested();
                var source = ValidationPaths.ContainedPath(Path.Combine(dataset, "photos"), photo.File);
                var destination = ValidationPaths.ContainedPath(staging, photo.File);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, false);
                if (new FileInfo(destination).Length != photo.Bytes || files.HashFile(destination) != photo.Sha256)
                {
                    throw new InvalidDataException($"Original photo failed checksum verification: {photo.File}.");
                }
            }
            var store = stores.Open(Path.Combine(output, "model.echo"));
            store.Init(fixture.Name);
            store.Import(staging, fixture.Dataset, int.MaxValue);
            var project = store.Load();
            if (!project.Images.Select(photo => photo.Hash).ToHashSet().SetEquals(selected))
            {
                throw new InvalidDataException("Import did not retain every pinned photograph.");
            }
            store.Edit(data => data.Settings = json.Clone(fixture.Settings));
            return store;
        }
        finally
        {
            Directory.Delete(staging, true);
        }
    }
}
