namespace Echo.Features;

public interface IGaussianBlur
{
    void Apply(ReadOnlySpan<float> source, Span<float> destination, int width, int height, double sigma, CancellationToken cancellation);
}
