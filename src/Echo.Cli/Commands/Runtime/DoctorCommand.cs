using Echo.Metadata;

namespace Echo.Cli.Commands.Runtime;

public sealed class DoctorCommand : ICliCommand
{
    private readonly IMetadataReader metadataReader;
    private readonly ICommandOutput output;

    public DoctorCommand(IMetadataReader metadataReader, ICommandOutput output)
    {
        this.metadataReader = metadataReader;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["doctor"];

    public int Execute(Arguments arguments)
    {
        var metadataEngine = metadataReader.Description;
        output.Write(new
        {
            schemaVersion = 1,
            version = "0.1.0",
            runtime = Environment.Version.ToString(),
            processors = Environment.ProcessorCount,
            metadataEngine,
            metadataReady = true,
            backend = "C# CPU; no external photogrammetry engine",
            limitations = new[] { "No lens calibration", "No 500-photo performance validation yet" }
        });
        return 0;
    }
}
