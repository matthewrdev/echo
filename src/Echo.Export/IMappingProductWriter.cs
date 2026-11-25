using Echo.Models;

namespace Echo.Export;

public interface IMappingProductWriter
{
    void ValidateCrs(string crs);

    MappingProducts Write(string localCloud, string modelRaster, string directory, GeoPosition origin,
        string crs, double resolution, bool relativeHeights);
}
