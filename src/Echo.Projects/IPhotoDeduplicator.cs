namespace Echo.Projects;

public interface IPhotoDeduplicator
{
    PhotoDeduplicationPlan Plan(IProjectStore store);
    void Restore(IProjectStore store, string imageId);
    void Apply(IProjectStore store, PhotoDeduplicationPlan plan);
}
