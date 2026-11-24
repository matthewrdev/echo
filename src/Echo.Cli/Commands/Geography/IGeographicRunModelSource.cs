using Echo.Models;
using Echo.Projects;

namespace Echo.Cli.Commands.Geography;

internal interface IGeographicRunModelSource
{
    GeographicModelSource? Resolve(IProjectStore store, RunData run, GeoPosition origin);
}
