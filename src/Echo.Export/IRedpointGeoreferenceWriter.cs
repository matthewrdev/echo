using Echo.Models;

namespace Echo.Export;

public interface IRedpointGeoreferenceWriter
{
    void Write(string directory, GeoPosition origin, Surface surface);
}
