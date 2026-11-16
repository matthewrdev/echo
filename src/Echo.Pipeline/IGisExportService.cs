using Echo.Projects;

namespace Echo.Pipeline;

public interface IGisExportService
{
    GisExportResult Export(IProjectStore store, string runId, string destination);
}
