using Echo.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Cli;

public sealed class CliServices
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IModelRenderer, NativeModelRenderer>();
        services.AddSingleton<IArgumentParser, ArgumentParser>();
        services.AddSingleton<ICommandApplication, CommandApplication>();
        services.AddSingleton<ICommandOutput, CommandOutput>();
        services.AddSingleton<ICliCommand, ConfigurationCommand>();
        services.AddSingleton<ICliCommand, DoctorCommand>();
        services.AddSingleton<ICliCommand, GisCommand>();
        services.AddSingleton<ICliCommand, GlbCommand>();
        services.AddSingleton<IHintFactory, HintFactory>();
        services.AddSingleton<ICliCommand, HintsCommand>();
        services.AddSingleton<ICliCommand, ImagesCommand>();
        services.AddSingleton<ICliCommand, InspectCommand>();
        services.AddSingleton<ICliCommand, PipelineCommand>();
        services.AddSingleton<ICliCommand, ProjectCommand>();
        services.AddSingleton<ICliCommand, RenderCommand>();
        services.AddSingleton<ICliCommand, RunCommand>();
        services.AddSingleton<ICliCommand, RuntimeInfoCommand>();
        services.AddSingleton<IRuntimeInfoProvider, RuntimeInfoProvider>();
        services.AddSingleton<ICliCommand, SetsCommand>();
        services.AddSingleton<ICliCommand, StorageCommand>();
        services.AddSingleton<ICliCommand, TraceCommand>();
    }
}
