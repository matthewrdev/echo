namespace Echo.Geometry;

public interface IRandomSampleSelector
{
    void Select<T>(IReadOnlyList<T> source, Random random, Span<T> sample);
}
