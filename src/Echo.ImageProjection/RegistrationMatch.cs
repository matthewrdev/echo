using Echo.Geometry;

namespace Echo.ImageProjection;

internal sealed record RegistrationMatch(double[] Pixel, V3 Position, double Score = 0);
