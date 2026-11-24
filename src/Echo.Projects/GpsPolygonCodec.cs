using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Projects;

internal static class GpsPolygonCodec
{
    public static List<GpsPolygon> Read(BinaryReader reader) => reader.ReadArray(() => new GpsPolygon
    {
        Id = reader.ReadString(),
        Mode = (CropRegionMode)reader.ReadInt32(),
        Vertices = reader.ReadArray(() => new GpsPolygonVertex(reader.ReadDouble(), reader.ReadDouble())).ToList()
    }).ToList();

    public static void Write(BinaryWriter writer, IReadOnlyList<GpsPolygon> polygons)
    {
        writer.WriteCollection(polygons, polygon =>
        {
            writer.Write(polygon.Id);
            writer.Write((int)polygon.Mode);
            writer.WriteCollection(polygon.Vertices, vertex =>
            {
                writer.Write(vertex.Latitude);
                writer.Write(vertex.Longitude);
            });
        });
    }
}
