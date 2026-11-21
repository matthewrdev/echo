using Echo.Models;

namespace Echo.Projects;

public sealed class ProjectManagementService : IProjectManagementService
{
    public ProjectManagementService()
    {
    }

    public ProjectData Rename(IProjectStore store, string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Any(char.IsControl))
        {
            throw new EchoException("Enter a project name without control characters.", 2);
        }

        using var run = store.Lock("run");
        return store.Edit(project => project.Name = trimmed);
    }

    public void Delete(IProjectStore store, string confirmation)
    {
        using var run = store.Lock("run");
        using var import = store.Lock("import");
        using var edit = store.Lock("edit");
        var project = store.Load();
        if (string.IsNullOrWhiteSpace(confirmation) || confirmation != project.Id)
        {
            throw new EchoException("Deletion requires --confirm with the current project ID.", 2);
        }

        var directory = new DirectoryInfo(store.Root);
        if (directory.Parent == null || directory.LinkTarget != null)
        {
            throw new EchoException("Cannot delete a filesystem root or a linked project folder.", 2);
        }

        Directory.Delete(store.Root, recursive: true);
    }
}
