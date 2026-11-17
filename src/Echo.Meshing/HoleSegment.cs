namespace Echo.Meshing;

internal readonly record struct HoleSegment(int Index, double MinimumX, double MaximumX, double MinimumY, double MaximumY);
