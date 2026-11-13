using Echo.Contracts;

namespace Echo.Depth;

internal readonly record struct DepthNeighbourCandidate(CameraPose Camera, double Distance);
