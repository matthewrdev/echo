namespace Echo.Depth;

public sealed record DepthSearchRange(double Near, double Far, double Median, int SampleCount, string Source);
