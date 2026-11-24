using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Telemetry;

internal sealed class PerformanceStatisticCodec
{
    public PerformanceStatistic Read(BinaryReader reader) => new()
    {
        BusyMilliseconds = reader.ReadOptional(reader.ReadDouble),
        MaximumConcurrency = reader.ReadOptional(reader.ReadInt32),
        MedianUpperMilliseconds = reader.ReadOptional(reader.ReadDouble),
        P95UpperMilliseconds = reader.ReadOptional(reader.ReadDouble),
        ThreadAllocatedBytes = reader.ReadOptional(reader.ReadInt64),
        ThreadAllocationSamples = reader.ReadInt64(),
        ProcessAllocatedBytes = reader.ReadOptional(reader.ReadInt64),
        ProcessCpuMilliseconds = reader.ReadOptional(reader.ReadDouble),
        GcPauseMilliseconds = reader.ReadOptional(reader.ReadDouble),
        Gen0Collections = reader.ReadOptional(reader.ReadInt64),
        Gen1Collections = reader.ReadOptional(reader.ReadInt64),
        Gen2Collections = reader.ReadOptional(reader.ReadInt64),
        Stage = reader.ReadString(),
        Name = reader.ReadString(),
        Kind = (PerformanceSpanKind)reader.ReadByte(),
        Backend = (PerformanceBackend)reader.ReadByte(),
        Count = reader.ReadInt64(),
        ReusedCount = reader.ReadInt64(),
        FailedCount = reader.ReadInt64(),
        TotalMilliseconds = reader.ReadDouble(),
        MaximumMilliseconds = reader.ReadDouble(),
        GpuMilliseconds = reader.ReadOptional(reader.ReadDouble),
        Items = reader.ReadInt64(),
        Bytes = reader.ReadInt64()
    };

    public void Write(BinaryWriter writer, PerformanceStatistic value)
    {
        writer.WriteOptional(value.BusyMilliseconds, writer.Write);
        writer.WriteOptional(value.MaximumConcurrency, writer.Write);
        writer.WriteOptional(value.MedianUpperMilliseconds, writer.Write);
        writer.WriteOptional(value.P95UpperMilliseconds, writer.Write);
        writer.WriteOptional(value.ThreadAllocatedBytes, writer.Write);
        writer.Write(value.ThreadAllocationSamples);
        writer.WriteOptional(value.ProcessAllocatedBytes, writer.Write);
        writer.WriteOptional(value.ProcessCpuMilliseconds, writer.Write);
        writer.WriteOptional(value.GcPauseMilliseconds, writer.Write);
        writer.WriteOptional(value.Gen0Collections, writer.Write);
        writer.WriteOptional(value.Gen1Collections, writer.Write);
        writer.WriteOptional(value.Gen2Collections, writer.Write);
        writer.Write(value.Stage);
        writer.Write(value.Name);
        writer.Write((byte)value.Kind);
        writer.Write((byte)value.Backend);
        writer.Write(value.Count);
        writer.Write(value.ReusedCount);
        writer.Write(value.FailedCount);
        writer.Write(value.TotalMilliseconds);
        writer.Write(value.MaximumMilliseconds);
        writer.WriteOptional(value.GpuMilliseconds, writer.Write);
        writer.Write(value.Items);
        writer.Write(value.Bytes);
    }
}
