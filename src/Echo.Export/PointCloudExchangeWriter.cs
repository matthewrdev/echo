namespace Echo.Export;

public sealed class PointCloudExchangeWriter : IPointCloudExchangeWriter
{
    public void Write(string path, PointCloudExportFormat format, int count, IEnumerable<GeographicCloudPoint> points)
    {
        using var writer = File.CreateText(path);
        switch (format)
        {
            case PointCloudExportFormat.Pts:
                writer.WriteLine(FormattableString.Invariant($"{count}"));
                foreach (var point in points)
                {
                    var p = point.Position;
                    writer.WriteLine(FormattableString.Invariant($"{p.X:R} {p.Y:R} {p.Z:R} 0 {point.Red} {point.Green} {point.Blue}"));
                }

                break;
            case PointCloudExportFormat.Xyz:
                foreach (var point in points)
                {
                    var p = point.Position;
                    writer.WriteLine(FormattableString.Invariant($"{p.X:R} {p.Y:R} {p.Z:R} {point.Red} {point.Green} {point.Blue}"));
                }

                break;
            case PointCloudExportFormat.Dxf:
                WriteDxf(writer, points);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private void WriteDxf(TextWriter writer, IEnumerable<GeographicCloudPoint> points)
    {
        writer.Write("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1018\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
        foreach (var point in points)
        {
            var p = point.Position;
            var colour = (point.Red << 16) | (point.Green << 8) | point.Blue;
            writer.Write(FormattableString.Invariant($"0\nPOINT\n100\nAcDbEntity\n8\n0\n420\n{colour}\n100\nAcDbPoint\n10\n{p.X:R}\n20\n{-p.Z:R}\n30\n{p.Y:R}\n"));
        }

        writer.Write("0\nENDSEC\n0\nEOF\n");
    }
}
