namespace Echo.Cli;

public sealed record RuntimeInfo
{
    public int SchemaVersion { get; init; } = 1;
    public required string Framework
    {
        get; init;
    }
    public required string RuntimeVersion
    {
        get; init;
    }
    public required string RuntimeIdentifier
    {
        get; init;
    }
    public required string ProcessArchitecture
    {
        get; init;
    }
    public required string OperatingSystemArchitecture
    {
        get; init;
    }
    public required string OperatingSystem
    {
        get; init;
    }
    public required string BuildConfiguration
    {
        get; init;
    }
    public required string Version
    {
        get; init;
    }
    public required string Executable
    {
        get; init;
    }
    public bool DynamicCodeSupported
    {
        get; init;
    }
    public bool DynamicCodeCompiled
    {
        get; init;
    }
    public bool ServerGC
    {
        get; init;
    }
    public required string GCLatency
    {
        get; init;
    }
    public long GCAvailableMemoryBytes
    {
        get; init;
    }
    public int ProcessorCount
    {
        get; init;
    }
    public bool Is64BitProcess
    {
        get; init;
    }
    public bool VectorAccelerated
    {
        get; init;
    }
    public int VectorFloatLanes
    {
        get; init;
    }
    public bool ArmAdvSimd
    {
        get; init;
    }
    public bool X86Avx2
    {
        get; init;
    }
    public string? ExifTool
    {
        get; init;
    }
    public required IReadOnlyDictionary<string, string> Assemblies
    {
        get; init;
    }
}
