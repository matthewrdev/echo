using Echo.Models;

namespace Echo.Projects;

public interface IProjectStore
{
    string Root
    {
        get;
    }

    string PathFor(string relative);
    ProjectData Load();
    void Init(string? name = null);
    FileStream Lock(string name);
    ProjectData Edit(Action<ProjectData> action);
    List<Photo> Import(string directory, string set, int limit, Action<object>? progress = null);
    List<Photo> Import(IReadOnlyList<PhotoImportEntry> entries, Action<object>? progress = null);
    void AddHints(List<Hint> hints);
    IReadOnlyList<RunSummary> ListRuns();
    void SaveRun(RunData run);
    RunData GetRun(string id = "latest");
}
