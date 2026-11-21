using Echo.Fusion;

namespace Echo.Pipeline.Fusion;

public sealed record FusionGridMetadata(double PixelFootprint, double RequestedVoxelSize, double VoxelSize,
    long Cells, int Spans, GridAxis Axis);
