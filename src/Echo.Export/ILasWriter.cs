using Echo.Models;

namespace Echo.Export;

public interface ILasWriter
{
    void Write(string path, GeoPosition origin, IEnumerable<GeographicCloudPoint> points);
}
