using Echo.Projects;

namespace Echo.Pipeline.Exporting;

public interface IGisExportService
{
    GisExportResult Export(IProjectStore store, string runId, string destination);
}
