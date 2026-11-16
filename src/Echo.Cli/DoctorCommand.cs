using System.ComponentModel.Composition;
using Echo.Metadata;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DoctorCommand : ICliCommand
{
    private readonly IMetadataReader metadataReader;
    private readonly ICommandOutput output;

    [ImportingConstructor]
    public DoctorCommand(IMetadataReader metadataReader, ICommandOutput output)
    {
        this.metadataReader = metadataReader;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["doctor"];

    public int Execute(Arguments arguments)
    {
        var metadataEngine = metadataReader.Description;
        output.Write(new DoctorReport
        {
            SchemaVersion = 1,
            Version = "0.1.0",
            Runtime = Environment.Version.ToString(),
            Processors = Environment.ProcessorCount,
            MetadataEngine = metadataEngine,
            MetadataReady = true,
            Backend = "C# CPU; no external photogrammetry engine",
            Limitations = new[]
            {
                "No lens calibration",
                "No 500-photo performance validation yet"
            }
        });
        return 0;
    }
}
