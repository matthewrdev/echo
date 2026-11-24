using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Telemetry;

internal sealed class PerformanceTraceHeaderCodec
{
    private const uint Magic = 0x31485445; // ETH1
    private readonly PerformanceTraceSummaryCodec summary = new();
    private readonly PerformanceSpanCodec spans = new();
    private readonly PerformanceStatisticCodec statistics = new();
    private readonly PerformanceMeasurementCodec measurements = new();

    public PerformanceTraceSummary ReadSummary(BinaryReader reader)
    {
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Unsupported binary trace header.");
        }

        return summary.Read(reader);
    }

    public PerformanceTraceHeader Read(BinaryReader reader) => new()
    {
        Summary = ReadSummary(reader),
        SpanBytes = reader.ReadInt64(),
        ActiveSpans = ReadList(reader, spans.Read),
        Statistics = ReadList(reader, statistics.Read),
        Measurements = ReadList(reader, measurements.Read)
    };

    public void Write(BinaryWriter writer, PerformanceTraceHeader header)
    {
        writer.Write(Magic);
        summary.Write(writer, header.Summary);
        writer.Write(header.SpanBytes);
        WriteList(writer, header.ActiveSpans, spans.Write);
        WriteList(writer, header.Statistics, statistics.Write);
        WriteList(writer, header.Measurements, measurements.Write);
    }

    private List<T> ReadList<T>(BinaryReader reader, Func<BinaryReader, T> read)
    {
        var count = reader.ReadCount();
        var values = new List<T>(count);
        for (var index = 0; index < count; index++)
        {
            values.Add(read(reader));
        }

        return values;
    }

    private void WriteList<T>(BinaryWriter writer, List<T> values, Action<BinaryWriter, T> write)
    {
        writer.Write(values.Count);
        foreach (var value in values)
        {
            write(writer, value);
        }
    }
}
