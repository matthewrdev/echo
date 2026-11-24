namespace Echo.Export;

public interface IPointCloudExchangeWriter
{
    void Write(string path, PointCloudExportFormat format, int count, IEnumerable<GeographicCloudPoint> points);
}
