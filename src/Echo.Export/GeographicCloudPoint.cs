using Echo.Geometry;

namespace Echo.Export;

public readonly record struct GeographicCloudPoint(V3 Position, byte Red, byte Green, byte Blue);
