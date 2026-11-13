namespace Echo.Optimization;

public interface IMatrix3Inverter
{
    void Invert(ReadOnlySpan<double> matrix, Span<double> inverse);
}
