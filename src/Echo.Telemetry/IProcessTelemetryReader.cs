namespace Echo.Telemetry;

public interface IProcessTelemetryReader
{
    ProcessCounters? Read(int processId);
}
