using Echo.Geometry;
using Echo.Models;

namespace Echo.Export.Terrain;

internal sealed record TerrainGrid(int Columns, int Rows, double West, double North, double LongitudeStep,
    double LatitudeStep, float[] Heights, string Imagery, GeoPosition Origin, byte[]? CellMask)
{
    public bool InFootprint(int column, int row) => CellMask == null || CellMask[row * (Columns - 1) + column] != 0;

    public double Column(double longitude) => (longitude - West) / LongitudeStep - .5;
    public double Row(double latitude) => (North - latitude) / LatitudeStep - .5;

    public GeoPosition Position(int index) => new()
    {
        Longitude = West + (index % Columns + .5) * LongitudeStep,
        Latitude = North - (index / Columns + .5) * LatitudeStep,
        Altitude = Heights[index],
        AltitudeDatum = Origin.AltitudeDatum
    };

    public V3 Local(int index) => CameraGeometry.ToLocal(Position(index), Origin);

    public double[] Uv(double column, double row) => [(column + .5) / Columns, (row + .5) / Rows];

    public double Elevation(double column, double row)
    {
        var inside = column >= 0 && row >= 0 && column < Columns - 1 && row < Rows - 1;
        if (!inside)
        {
            throw new EchoException("A wall extends past the usable elevation raster.", 2);
        }

        var x = (int)column;
        var y = (int)row;
        var fx = column - x;
        var fy = row - y;
        var cell = y * Columns + x;
        var top = Heights[cell] * (1 - fx) + Heights[cell + 1] * fx;
        var bottom = Heights[cell + Columns] * (1 - fx) + Heights[cell + Columns + 1] * fx;
        return top * (1 - fy) + bottom * fy;
    }
}
