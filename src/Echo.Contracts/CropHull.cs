namespace Echo.Contracts;

public sealed class CropHull
{
    // Matching near/far rings in world coordinates, followed by outward-facing planes.
    public double[][] Vertices { get; set; } = [];
    public CropPlane[] Planes { get; set; } = [];
    public double Tolerance { get; set; } = 0.000001;

    public void Validate()
    {
        var validVertices = Vertices != null && Vertices.Length is >= 6 and <= 256 && Vertices.Length % 2 == 0
            && Vertices.All(vertex => vertex != null && vertex.Length == 3 && vertex.All(double.IsFinite));
        var validPlanes = Planes != null && Planes.Length is >= 5 and <= 130 && Planes.All(ValidPlane);
        var validTolerance = double.IsFinite(Tolerance) && Tolerance > 0;
        if (!validVertices || !validPlanes || !validTolerance)
        {
            throw new EchoException("Painted crop hulls need finite near/far rings, unit plane normals and a positive tolerance.", 2);
        }
        if (Planes!.Length != Vertices!.Length / 2 + 2 || Vertices.Any(vertex => Planes.Any(plane =>
            vertex[0] * plane.NormalX + vertex[1] * plane.NormalY + vertex[2] * plane.NormalZ > plane.Offset + Tolerance)))
        {
            throw new EchoException("Painted crop hull vertices must lie inside their boundary planes.", 2);
        }
    }

    private bool ValidPlane(CropPlane plane)
    {
        if (plane == null || !double.IsFinite(plane.Offset))
        {
            return false;
        }
        var lengthSquared = plane.NormalX * plane.NormalX + plane.NormalY * plane.NormalY + plane.NormalZ * plane.NormalZ;
        return double.IsFinite(lengthSquared) && Math.Abs(lengthSquared - 1) < 0.000001;
    }
}
