using Echo.Contracts;
using Echo.Projects;

namespace Echo.Pipeline;

public interface IArtifactStorageService
{
    ArtifactStorageInventory Inspect(IProjectStore store);
    ArtifactDeletionPlan Plan(IProjectStore store, string? artifact);
    ArtifactDeletionPlan Delete(IProjectStore store, string? artifact, string confirmationToken);
}
