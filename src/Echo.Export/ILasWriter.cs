using Echo.Models;

namespace Echo.Export;

public interface ILasWriter
{
    void WriteLocal(string path, IEnumerable<GeographicCloudPoint> points);
    void Write(string path, GeoPosition origin, IEnumerable<GeographicCloudPoint> points);
}
