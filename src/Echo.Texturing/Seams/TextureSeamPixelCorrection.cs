using Echo.Geometry;

namespace Echo.Texturing.Seams;

internal readonly record struct TextureSeamPixelCorrection(V3 Sum, double Weight);
