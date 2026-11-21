using Echo.Models;

namespace Echo.Projects;

public interface IPhotoDeletionService
{
    ProjectData Delete(IProjectStore store, IReadOnlyCollection<string> imageIds);
}
