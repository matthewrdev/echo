using Echo.Geometry;

namespace Echo.Texturing;

internal readonly record struct TextureChartProjection(CameraProjector? Camera, V3 Origin, V3 Right, V3 Down, double Scale)
{
    public TextureCoordinate? Project(V3 point)
    {
        if (Camera is not null)
        {
            var pixel = Camera.Project(point);
            return pixel.HasValue
                ? new TextureCoordinate((pixel.Value.X - Origin.X) * Scale, (pixel.Value.Y - Origin.Y) * Scale)
                : null;
        }

        var offset = point - Origin;
        return new TextureCoordinate(offset.Dot(Right), offset.Dot(Down));
    }
}
