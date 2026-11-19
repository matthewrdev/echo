namespace Echo.Benchmarks;

public sealed record CloudPublicationMeasurement(string Implementation, int Trial, double Milliseconds, long ChecksumRereadBytes, string Sha256);
