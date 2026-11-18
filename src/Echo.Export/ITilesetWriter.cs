using Echo.Contracts;

namespace Echo.Export;

public interface ITilesetWriter
{
    void Write(string directory, GeoPosition origin, Surface surface);
}
