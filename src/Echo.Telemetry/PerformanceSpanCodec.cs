using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Telemetry;

internal sealed class PerformanceSpanCodec : IBinaryJournalCodec<PerformanceSpan>
{
    public uint Magic => 0x31535445; // ETS1

    public PerformanceSpan Read(BinaryReader reader) => new()
    {
        ThreadAllocatedBytes = reader.ReadOptional(reader.ReadInt64),
        ProcessAllocatedBytes = reader.ReadOptional(reader.ReadInt64),
        ProcessCpuMilliseconds = reader.ReadOptional(reader.ReadDouble),
        GcPauseMilliseconds = reader.ReadOptional(reader.ReadDouble),
        Gen0Collections = reader.ReadOptional(reader.ReadInt64),
        Gen1Collections = reader.ReadOptional(reader.ReadInt64),
        Gen2Collections = reader.ReadOptional(reader.ReadInt64),
        Id = reader.ReadInt64(),
        ParentId = reader.ReadOptional(reader.ReadInt64),
        Stage = reader.ReadString(),
        Name = reader.ReadString(),
        Kind = (PerformanceSpanKind)reader.ReadByte(),
        Backend = (PerformanceBackend)reader.ReadByte(),
        Status = (PerformanceStatus)reader.ReadByte(),
        ThreadId = reader.ReadInt32(),
        StartMilliseconds = reader.ReadDouble(),
        DurationMilliseconds = reader.ReadDouble(),
        GpuMilliseconds = reader.ReadOptional(reader.ReadDouble),
        Items = reader.ReadOptional(reader.ReadInt64),
        TotalItems = reader.ReadOptional(reader.ReadInt64),
        Unit = reader.ReadString(),
        Bytes = reader.ReadInt64(),
        Detail = reader.ReadOptionalString()
    };

    public void Write(BinaryWriter writer, PerformanceSpan value)
    {
        writer.WriteOptional(value.ThreadAllocatedBytes, writer.Write);
        writer.WriteOptional(value.ProcessAllocatedBytes, writer.Write);
        writer.WriteOptional(value.ProcessCpuMilliseconds, writer.Write);
        writer.WriteOptional(value.GcPauseMilliseconds, writer.Write);
        writer.WriteOptional(value.Gen0Collections, writer.Write);
        writer.WriteOptional(value.Gen1Collections, writer.Write);
        writer.WriteOptional(value.Gen2Collections, writer.Write);
        writer.Write(value.Id);
        writer.WriteOptional(value.ParentId, writer.Write);
        writer.Write(value.Stage);
        writer.Write(value.Name);
        writer.Write((byte)value.Kind);
        writer.Write((byte)value.Backend);
        writer.Write((byte)value.Status);
        writer.Write(value.ThreadId);
        writer.Write(value.StartMilliseconds);
        writer.Write(value.DurationMilliseconds);
        writer.WriteOptional(value.GpuMilliseconds, writer.Write);
        writer.WriteOptional(value.Items, writer.Write);
        writer.WriteOptional(value.TotalItems, writer.Write);
        writer.Write(value.Unit);
        writer.Write(value.Bytes);
        writer.WriteOptionalString(value.Detail);
    }
}
