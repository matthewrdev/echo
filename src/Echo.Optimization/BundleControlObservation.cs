using Echo.Geometry;

namespace Echo.Optimization;

internal sealed record BundleControlObservation(int Camera, V3 Position, double[] Pixel);
