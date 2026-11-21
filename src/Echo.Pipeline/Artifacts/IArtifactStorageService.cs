using Echo.Models;
using Echo.Projects;

namespace Echo.Pipeline.Artifacts;

public interface IArtifactStorageService
{
    ArtifactStorageInventory Inspect(IProjectStore store);
    ArtifactDeletionPlan Plan(IProjectStore store, ArtifactDeletionRequest request);
    ArtifactDeletionPlan Delete(IProjectStore store, ArtifactDeletionRequest request, string confirmationToken);
}
