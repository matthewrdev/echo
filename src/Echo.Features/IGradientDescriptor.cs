namespace Echo.Features;

public interface IGradientDescriptor
{
    int Orientations(ReadOnlySpan<float> pixels, int width, int height, ScaleKeypoint point, Span<double> angles);
    bool Describe(ReadOnlySpan<float> pixels, int width, int height, ScaleKeypoint point, double angle, Span<float> descriptor);
}
