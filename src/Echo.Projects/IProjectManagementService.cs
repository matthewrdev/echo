using Echo.Contracts;

namespace Echo.Projects;

public interface IProjectManagementService
{
    ProjectData Rename(IProjectStore store, string name);
    void Delete(IProjectStore store, string confirmation);
}
