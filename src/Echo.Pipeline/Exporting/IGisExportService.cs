using Echo.Models;
using Echo.Projects;

namespace Echo.Pipeline.Exporting;

public interface IGisExportService
{
    GisExportResult Export(IProjectStore store, string runId, string destination,
        Action<RunData, GeoPosition, string>? writeRaster = null);
}
