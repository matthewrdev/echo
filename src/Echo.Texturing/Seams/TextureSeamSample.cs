using Echo.Geometry;

namespace Echo.Texturing.Seams;

internal readonly record struct TextureSeamSample(
    int Node,
    V3 Position,
    double Weight);
