using Echo.Cli.Commands.Configuration;
using Echo.Cli.Commands.Export;
using Echo.Cli.Commands.Hints;
using Echo.Cli.Commands.Images;
using Echo.Cli.Commands.Processing;
using Echo.Cli.Commands.Projects;
using Echo.Cli.Commands.Runtime;
using Echo.Cli.Commands.Storage;
using Echo.Cli.Commands.Tracing;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Cli;

public static class CliServiceCollectionExtensions
{
    public static IServiceCollection AddCli(this IServiceCollection services)
    {
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

        return services;
    }
}
