using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Telemetry;

internal sealed class PerformanceTraceSummaryCodec
{
    public PerformanceTraceSummary Read(BinaryReader reader) => new()
    {
        DroppedMeasurements = reader.ReadInt64(),
        Environment = ReadEnvironment(reader),
        Id = reader.ReadString(),
        RunId = reader.ReadString(),
        Target = reader.ReadString(),
        StartedAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero),
        Status = (PerformanceStatus)reader.ReadByte(),
        DurationMilliseconds = reader.ReadDouble(),
        CpuMilliseconds = reader.ReadDouble(),
        DroppedSpans = reader.ReadInt64()
    };

    public void Write(BinaryWriter writer, PerformanceTraceSummary value)
    {
        writer.Write(value.DroppedMeasurements);
        WriteEnvironment(writer, value.Environment);
        writer.Write(value.Id);
        writer.Write(value.RunId);
        writer.Write(value.Target);
        writer.Write(value.StartedAt.UtcTicks);
        writer.Write(value.UpdatedAt.UtcTicks);
        writer.Write((byte)value.Status);
        writer.Write(value.DurationMilliseconds);
        writer.Write(value.CpuMilliseconds);
        writer.Write(value.DroppedSpans);
    }

    private PerformanceTraceEnvironment? ReadEnvironment(BinaryReader reader)
    {
        return reader.ReadBoolean()
            ? new PerformanceTraceEnvironment(reader.ReadString(), reader.ReadString(), reader.ReadString(),
                reader.ReadInt32(), reader.ReadBoolean())
            : null;
    }

    private void WriteEnvironment(BinaryWriter writer, PerformanceTraceEnvironment? value)
    {
        writer.Write(value != null);
        if (value != null)
        {
            writer.Write(value.Runtime);
            writer.Write(value.OperatingSystem);
            writer.Write(value.Architecture);
            writer.Write(value.LogicalProcessors);
            writer.Write(value.ServerGc);
        }
    }
}
