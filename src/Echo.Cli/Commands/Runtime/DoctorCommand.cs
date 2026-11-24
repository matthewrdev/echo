using Echo.Export;
using Echo.Metadata;

namespace Echo.Cli.Commands.Runtime;

public sealed class DoctorCommand : ICliCommand
{
    private readonly IMetadataReader metadataReader;
    private readonly ICommandOutput output;
    private readonly ISurveyCloudWriter survey;

    public DoctorCommand(IMetadataReader metadataReader, ICommandOutput output, ISurveyCloudWriter survey)
    {
        this.metadataReader = metadataReader;
        this.output = output;
        this.survey = survey;
    }

    public IReadOnlyCollection<string> Names => ["doctor"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("doctor",
            "doctor",
            "Check processing dependencies and capabilities."),
    ];

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
            surveyExports = survey.Inspect(),
            backend = "C# CPU; no external photogrammetry engine",
            limitations = new[] { "No lens calibration", "No 500-photo performance validation yet" }
        });
        return 0;
    }
}
