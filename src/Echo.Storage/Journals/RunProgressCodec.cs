using System.Text;
using Echo.Models;

namespace Echo.Storage.Journals;

public sealed class RunProgressCodec
{
    private const uint Magic = 0x31505245; // ERP1

    public RunProgress Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Unsupported run progress snapshot.");
        }

        return ReadRunProgress(reader);
    }

    public void Write(Stream stream, RunProgress value)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        WriteRunProgress(writer, value);
    }

    private RunProgress ReadRunProgress(BinaryReader reader) => new()
    {
        StageSteps = reader.ReadArray(reader.ReadString),
        StageStepIndex = reader.ReadOptional(reader.ReadInt32),
        Steps = reader.ReadArray(reader.ReadString),
        StepIndex = reader.ReadOptional(reader.ReadInt32),
        Resources = reader.ReadBoolean() ? ReadResourceProgress(reader) : null,
        Operation = reader.ReadString(),
        Unit = reader.ReadString(),
        OperationStarted = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero),
        Phase = reader.ReadString(),
        Stage = reader.ReadString(),
        Message = reader.ReadString(),
        Completed = reader.ReadOptional(reader.ReadInt64),
        Total = reader.ReadOptional(reader.ReadInt64),
        Updated = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero)
    };

    private void WriteRunProgress(BinaryWriter writer, RunProgress value)
    {
        writer.WriteCollection(value.StageSteps, writer.Write);
        writer.WriteOptional(value.StageStepIndex, writer.Write);
        writer.WriteCollection(value.Steps, writer.Write);
        writer.WriteOptional(value.StepIndex, writer.Write);
        writer.Write(value.Resources != null);
        if (value.Resources is { } resources)
        {
            WriteResourceProgress(writer, resources);
        }

        writer.Write(value.Operation);
        writer.Write(value.Unit);
        writer.Write(value.OperationStarted.UtcTicks);
        writer.Write(value.Phase);
        writer.Write(value.Stage);
        writer.Write(value.Message);
        writer.WriteOptional(value.Completed, writer.Write);
        writer.WriteOptional(value.Total, writer.Write);
        writer.Write(value.Updated.UtcTicks);
    }

    private ResourceProgress ReadResourceProgress(BinaryReader reader) => new()
    {
        Processor = reader.ReadBoolean() ? ReadProcessorProgress(reader) : null,
        SampledAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero),
        WorkingSetBytes = reader.ReadInt64(),
        PeakWorkingSetBytes = reader.ReadInt64(),
        ManagedBytes = reader.ReadInt64(),
        MemoryBudgetBytes = reader.ReadInt64(),
        CacheResidentBytes = reader.ReadInt64(),
        CachePeakBytes = reader.ReadInt64(),
        DiskReadBytes = reader.ReadInt64(),
        DiskWrittenBytes = reader.ReadInt64(),
        PageEvictions = reader.ReadInt64(),
        ScratchBytes = reader.ReadInt64()
    };

    private void WriteResourceProgress(BinaryWriter writer, ResourceProgress value)
    {
        writer.Write(value.Processor != null);
        if (value.Processor is { } processor)
        {
            WriteProcessorProgress(writer, processor);
        }

        writer.Write(value.SampledAt.UtcTicks);
        writer.Write(value.WorkingSetBytes);
        writer.Write(value.PeakWorkingSetBytes);
        writer.Write(value.ManagedBytes);
        writer.Write(value.MemoryBudgetBytes);
        writer.Write(value.CacheResidentBytes);
        writer.Write(value.CachePeakBytes);
        writer.Write(value.DiskReadBytes);
        writer.Write(value.DiskWrittenBytes);
        writer.Write(value.PageEvictions);
        writer.Write(value.ScratchBytes);
    }

    private ProcessorProgress ReadProcessorProgress(BinaryReader reader) => new()
    {
        CpuPercent = reader.ReadOptional(reader.ReadDouble),
        LogicalProcessors = reader.ReadInt32(),
        Threads = reader.ReadBoolean() ? reader.ReadArray(() => ReadThreadProgress(reader)).ToList() : null
    };

    private void WriteProcessorProgress(BinaryWriter writer, ProcessorProgress value)
    {
        writer.WriteOptional(value.CpuPercent, writer.Write);
        writer.Write(value.LogicalProcessors);
        writer.Write(value.Threads != null);
        if (value.Threads is { } threads)
        {
            writer.WriteCollection(threads, item => WriteThreadProgress(writer, item));
        }

    }

    private ThreadProgress ReadThreadProgress(BinaryReader reader) => new()
    {
        ProcessId = reader.ReadInt32(),
        ThreadId = reader.ReadUInt64(),
        Name = reader.ReadString(),
        CpuPercent = reader.ReadDouble()
    };

    private void WriteThreadProgress(BinaryWriter writer, ThreadProgress value)
    {
        writer.Write(value.ProcessId);
        writer.Write(value.ThreadId);
        writer.Write(value.Name);
        writer.Write(value.CpuPercent);
    }

}
