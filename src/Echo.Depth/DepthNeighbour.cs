using Echo.Models;

namespace Echo.Depth;

internal sealed record DepthNeighbour(CameraPose Camera, Photo Photo, double[] WorldToCamera);
