using Echo.Contracts;

namespace Echo.Geometry;

public interface ICameraGeometry
{
    V3 Rotate(double[] rotation, V3 vector);
    double[] Transpose(double[] rotation);
    double[] Multiply(double[] leftMatrix, double[] rightMatrix);
    void Multiply(ReadOnlySpan<double> leftMatrix, ReadOnlySpan<double> rightMatrix, Span<double> result);
    double[] AttitudeRotation(Attitude attitude);
    V3 ToLocal(GeoPosition position, GeoPosition origin);
    double[] LocalToEcefMatrix(GeoPosition origin);
    V3 Ecef(GeoPosition position);
    double Focal(Photo photo, CameraPose? camera = null);
    V3 Ray(Photo photo, double[] pixel, CameraPose? camera = null);
    V3? ProjectPoint(Photo photo, CameraPose pose, V3 point);
    double[]? Project(Photo photo, CameraPose pose, V3 point);
    Triangulated? Triangulate(Photo leftPhoto, Photo rightPhoto, CameraPose leftCamera, CameraPose rightCamera, Match match, double maximumReprojectionErrorPixels = 12);
    double Square(double x);
    RelativePose? EstimateRelative(Photo leftPhoto, Photo rightPhoto, List<Match> matches);
}
