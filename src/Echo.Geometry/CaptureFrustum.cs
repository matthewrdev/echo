namespace Echo.Geometry;

public sealed class CaptureFrustum
{
    public CaptureFrustum(V3 centre, double[] rotation, double halfWidthRatio, double halfHeightRatio, double depth, double uncertainty)
    {
        Centre = centre;
        Right = new V3(rotation[0], rotation[3], rotation[6]);
        Down = new V3(rotation[1], rotation[4], rotation[7]);
        Forward = new V3(rotation[2], rotation[5], rotation[8]);
        HalfWidthRatio = halfWidthRatio;
        HalfHeightRatio = halfHeightRatio;
        Depth = depth;
        Uncertainty = uncertainty;
        Vertices =
        [
            centre,
            Point(-1, -1, depth), Point(1, -1, depth),
            Point(1, 1, depth), Point(-1, 1, depth)
        ];
        FaceNormals =
        [
            Forward,
            (Right - Forward * halfWidthRatio).Unit,
            (Right + Forward * halfWidthRatio).Unit,
            (Down - Forward * halfHeightRatio).Unit,
            (Down + Forward * halfHeightRatio).Unit
        ];
        EdgeDirections =
        [
            Right, Down,
            (Vertices[1] - centre).Unit, (Vertices[2] - centre).Unit,
            (Vertices[3] - centre).Unit, (Vertices[4] - centre).Unit
        ];
        Radius = (Vertices[1] - centre).Length;
    }

    public V3 Centre
    {
        get;
    }
    public V3 Right
    {
        get;
    }
    public V3 Down
    {
        get;
    }
    public V3 Forward
    {
        get;
    }
    public double HalfWidthRatio
    {
        get;
    }
    public double HalfHeightRatio
    {
        get;
    }
    public double Depth
    {
        get;
    }
    public double Uncertainty
    {
        get;
    }
    public double Radius
    {
        get;
    }
    public V3[] Vertices
    {
        get;
    }
    public V3[] FaceNormals
    {
        get;
    }
    public V3[] EdgeDirections
    {
        get;
    }

    public V3 Point(double horizontalFraction, double verticalFraction, double depth)
    {
        return Centre + (Forward + Right * (horizontalFraction * HalfWidthRatio)
            + Down * (verticalFraction * HalfHeightRatio)) * depth;
    }

    public bool Contains(V3 point)
    {
        var offset = point - Centre;
        var depth = offset.Dot(Forward);
        var insideDepth = depth >= 0 && depth <= Depth;
        var insideSides = Math.Abs(offset.Dot(Right)) <= depth * HalfWidthRatio
            && Math.Abs(offset.Dot(Down)) <= depth * HalfHeightRatio;
        return insideDepth && insideSides;
    }
}
