using System.ComponentModel.Composition;
using Echo.Contracts;

namespace Echo.Projects;

[Export(typeof(IPhotoDeletionService)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PhotoDeletionService : IPhotoDeletionService
{
    [ImportingConstructor]
    public PhotoDeletionService()
    {
    }

    public ProjectData Delete(IProjectStore store, IReadOnlyCollection<string> imageIds)
    {
        var ids = imageIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            throw new EchoException("Select at least one photo to delete.", 2);
        }

        using var run = store.Lock("run");
        using var import = store.Lock("import");
        var sources = store.PathFor("sources");
        var previews = store.PathFor("previews");
        var runFiles = SavedInputFiles(store);
        var removable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = store.Edit(project =>
        {
            var selected = project.Images.Where(photo => ids.Contains(photo.Id)).ToArray();
            if (selected.Length != ids.Count)
            {
                throw new EchoException("The selection contains an unknown image ID.", 2);
            }

            project.Images.RemoveAll(photo => ids.Contains(photo.Id));
            project.Hints.RemoveAll(hint => ids.Contains(hint.ImageId) || hint.OtherImageId != null && ids.Contains(hint.OtherImageId));
            foreach (var photo in project.Images)
            {
                if (photo.DuplicateOf != null && ids.Contains(photo.DuplicateOf))
                {
                    photo.DuplicateOf = null;
                }
            }

            var kept = project.Images.SelectMany(photo => InputPaths(store, photo)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            kept.UnionWith(runFiles);
            foreach (var path in selected.SelectMany(photo => InputPaths(store, photo)).Where(path => !kept.Contains(path)))
            {
                if (IsOwnedFile(path, sources) || IsOwnedFile(path, previews))
                {
                    removable.Add(path);
                }
            }

            if (Directory.Exists(sources) && new DirectoryInfo(sources).LinkTarget == null)
            {
                var prefixes = selected.Select(photo => photo.Id + ".original.").ToArray();
                foreach (var path in Directory.EnumerateFiles(sources))
                {
                    if (prefixes.Any(prefix => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        removable.Add(path);
                    }
                }
            }
        });

        // Commit the inventory first so a filesystem failure cannot leave a live photo with deleted inputs.
        var filesNotRemoved = 0;
        foreach (var path in removable)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                filesNotRemoved++;
            }
        }

        if (filesNotRemoved > 0)
        {
            throw new EchoException($"Photos removed from the dataset. {filesNotRemoved} unreferenced project files could not be deleted.", 4);
        }

        return result;
    }

    private HashSet<string> SavedInputFiles(IProjectStore store)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runs = store.PathFor("runs");
        if (Directory.Exists(runs))
        {
            foreach (var directory in Directory.EnumerateDirectories(runs))
            {
                if (File.Exists(Path.Combine(directory, "run.json")))
                {
                    var snapshot = store.GetRun(Path.GetFileName(directory)).Snapshot;
                    result.UnionWith(snapshot.Images.SelectMany(photo => InputPaths(store, photo)));
                }
            }
        }

        return result;
    }

    private IEnumerable<string> InputPaths(IProjectStore store, Photo photo)
    {
        return new[] { photo.Source, photo.Preview }
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => Path.GetFullPath(store.PathFor(path)));
    }

    private bool IsOwnedFile(string path, string directory)
    {
        return string.Equals(Path.GetDirectoryName(path), directory, StringComparison.Ordinal)
            && new DirectoryInfo(directory).LinkTarget == null;
    }
}
