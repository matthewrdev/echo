using Echo.Contracts;

namespace Echo.Depth;

internal sealed record DepthNeighbour(CameraPose Camera, Photo Photo, double[] WorldToCamera);
