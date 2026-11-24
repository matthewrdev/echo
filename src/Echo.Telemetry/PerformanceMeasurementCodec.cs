using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Telemetry;

internal sealed class PerformanceMeasurementCodec
{
    public PerformanceMeasurement Read(BinaryReader reader) => new()
    {
        Stage = reader.ReadString(),
        Name = reader.ReadString(),
        Unit = reader.ReadString(),
        Count = reader.ReadInt64(),
        Total = reader.ReadDouble(),
        Minimum = reader.ReadDouble(),
        Maximum = reader.ReadDouble()
    };

    public void Write(BinaryWriter writer, PerformanceMeasurement value)
    {
        writer.Write(value.Stage);
        writer.Write(value.Name);
        writer.Write(value.Unit);
        writer.Write(value.Count);
        writer.Write(value.Total);
        writer.Write(value.Minimum);
        writer.Write(value.Maximum);
    }
}
