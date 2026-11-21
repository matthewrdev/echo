using System.Runtime.Intrinsics;
using Echo.Geometry;

namespace Echo.Simplification;

internal readonly struct EdgeQuadric
{
    private readonly double q00;
    private readonly double q01;
    private readonly double q02;
    private readonly double q03;
    private readonly double q11;
    private readonly double q12;
    private readonly double q13;
    private readonly double q22;
    private readonly double q23;
    private readonly double q33;

    public EdgeQuadric(in Vertex first, in Vertex second)
    {
        q00 = first[0] + second[0];
        q01 = first[1] + second[1];
        q02 = first[2] + second[2];
        q03 = first[3] + second[3];
        q11 = first[5] + second[5];
        q12 = first[6] + second[6];
        q13 = first[7] + second[7];
        q22 = first[10] + second[10];
        q23 = first[11] + second[11];
        q33 = first[15] + second[15];
    }

    public void Evaluate(ReadOnlySpan<V3> points, Span<double> errors)
    {
        var start = 0;
        if (Vector128.IsHardwareAccelerated && points.Length >= 2)
        {
            var pair = EvaluatePair(points[0], points[1]);
            errors[0] = pair.GetElement(0);
            errors[1] = pair.GetElement(1);
            start = 2;
        }

        for (var index = start; index < points.Length; index++)
        {
            errors[index] = Evaluate(points[index]);
        }
    }

    public double Evaluate(V3 point)
    {
        double error = 0;
        // Keep the original 16-term accumulation order, including both halves.
        error += point.X * point.X * q00;
        error += point.X * point.Y * q01;
        error += point.X * point.Z * q02;
        error += point.X * 1d * q03;
        error += point.Y * point.X * q01;
        error += point.Y * point.Y * q11;
        error += point.Y * point.Z * q12;
        error += point.Y * 1d * q13;
        error += point.Z * point.X * q02;
        error += point.Z * point.Y * q12;
        error += point.Z * point.Z * q22;
        error += point.Z * 1d * q23;
        error += 1d * point.X * q03;
        error += 1d * point.Y * q13;
        error += 1d * point.Z * q23;
        error += 1d * 1d * q33;
        return error;
    }

    private Vector128<double> EvaluatePair(V3 first, V3 second)
    {
        var x = Vector128.Create(first.X, second.X);
        var y = Vector128.Create(first.Y, second.Y);
        var z = Vector128.Create(first.Z, second.Z);
        var one = Vector128.Create(1d);
        var error = Vector128<double>.Zero;
        // Keep the original 16-term accumulation order, including both halves.
        error += x * x * Vector128.Create(q00);
        error += x * y * Vector128.Create(q01);
        error += x * z * Vector128.Create(q02);
        error += x * one * Vector128.Create(q03);
        error += y * x * Vector128.Create(q01);
        error += y * y * Vector128.Create(q11);
        error += y * z * Vector128.Create(q12);
        error += y * one * Vector128.Create(q13);
        error += z * x * Vector128.Create(q02);
        error += z * y * Vector128.Create(q12);
        error += z * z * Vector128.Create(q22);
        error += z * one * Vector128.Create(q23);
        error += one * x * Vector128.Create(q03);
        error += one * y * Vector128.Create(q13);
        error += one * z * Vector128.Create(q23);
        error += one * one * Vector128.Create(q33);
        return error;
    }
}
