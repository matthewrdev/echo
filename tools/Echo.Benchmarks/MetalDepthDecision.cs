namespace Echo.Benchmarks;

internal readonly record struct MetalDepthDecision(bool Accepted, int BestPlane, float Depth, float Confidence);
