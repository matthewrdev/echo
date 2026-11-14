using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Imaging;

namespace Echo.Projects;

[Export(typeof(IPhotoDeduplicator)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PhotoDeduplicator : IPhotoDeduplicator
{
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public PhotoDeduplicator(IRasterImageService images)
    {
        this.images = images;
    }

    public PhotoDeduplicationPlan Plan(IProjectStore store)
    {
        var project = store.Load();
        var protectedIds = project.Hints.Where(hint => hint.Enabled)
            .SelectMany(hint => new[] { hint.ImageId, hint.OtherImageId }).ToHashSet();
        var samples = new List<PhotoSimilaritySample>();
        foreach (var photo in project.Images.Where(photo => photo.Enabled))
        {
            var preview = store.PathFor(photo.Preview);
            using var bitmap = images.Decode(File.Exists(preview) ? preview : store.PathFor(photo.Source));
            var gray = images.ResizeGray(bitmap, 320);
            samples.Add(new PhotoSimilaritySample(photo, gray, images.ResizeGray(bitmap, 80), PhotoSimilarity.Sharpness(gray), protectedIds.Contains(photo.Id) || photo.KeepWhenTrimming));
        }
        var ordered = samples.OrderByDescending(sample => sample.Protected)
            .ThenByDescending(sample => sample.Sharpness)
            .ThenBy(sample => sample.Photo.CapturedAt, StringComparer.Ordinal)
            .ThenBy(sample => sample.Photo.Name, StringComparer.Ordinal)
            .ThenBy(sample => sample.Photo.Id, StringComparer.Ordinal).ToArray();
        var excluded = new HashSet<string>();
        var groups = new List<PhotoDuplicateGroup>();
        foreach (var keeper in ordered)
        {
            if (excluded.Contains(keeper.Photo.Id) || keeper.Photo.KeepWhenTrimming)
            {
                continue;
            }
            var duplicates = new List<Photo>();
            foreach (var candidate in ordered)
            {
                var unavailable = candidate.Protected || candidate.Photo.Id == keeper.Photo.Id
                    || excluded.Contains(candidate.Photo.Id);
                if (unavailable || !PhotoSimilarity.Matches(keeper, candidate))
                {
                    continue;
                }
                excluded.Add(candidate.Photo.Id);
                duplicates.Add(candidate.Photo);
            }
            if (duplicates.Count > 0)
            {
                groups.Add(new PhotoDuplicateGroup(keeper.Photo, duplicates));
            }
        }
        return new PhotoDeduplicationPlan(project.Revision, samples.Count, groups);
    }

    public void Restore(IProjectStore store, string imageId)
    {
        using var runLease = store.Lock("run");
        store.Edit(project =>
        {
            var photo = project.Images.SingleOrDefault(image => image.Id == imageId)
                ?? throw new EchoException("Unknown image ID.", 2);
            photo.Enabled = true;
            photo.Discarded = false;
            photo.DuplicateOf = null;
            photo.KeepWhenTrimming = true;
        });
    }

    public void Apply(IProjectStore store, PhotoDeduplicationPlan plan)
    {
        using var runLease = store.Lock("run");
        if (plan.DuplicateCount == 0)
        {
            return;
        }
        var keepers = new Dictionary<string, string>();
        foreach (var group in plan.Groups)
        {
            foreach (var duplicate in group.Duplicates)
            {
                keepers.Add(duplicate.Id, group.Keep.Id);
            }
        }
        store.Edit(project =>
        {
            if (project.Revision != plan.Revision)
            {
                throw new EchoException("The photo selection changed. Scan for similar photos again.", 6);
            }
            foreach (var photo in project.Images.Where(photo => keepers.ContainsKey(photo.Id)))
            {
                photo.Enabled = false;
                photo.Discarded = true;
                photo.DuplicateOf = keepers[photo.Id];
            }
        });
    }
}
