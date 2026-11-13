using Echo.Contracts;

namespace Echo.Geometry;

public interface ICameraGeometry
{
    V3 Rotate(double[] r, V3 p);
    double[] Transpose(double[] r);
    double[] Multiply(double[] a, double[] b);
    double[] AttitudeRotation(Attitude a);
    V3 ToLocal(GeoPosition position, GeoPosition origin);
    double[] LocalToEcefMatrix(GeoPosition origin);
    V3 Ecef(GeoPosition p);
    double Focal(Photo photo, CameraPose? camera = null);
    V3 Ray(Photo photo, double[] pixel, CameraPose? camera = null);
    double[]? Project(Photo photo, CameraPose pose, V3 point);
    Triangulated? Triangulate(Photo a, Photo b, CameraPose ca, CameraPose cb, Match match, double maxError = 12);
    double Square(double x);
    RelativePose? EstimateRelative(Photo a, Photo b, List<Match> matches);
}
