using Microsoft.Extensions.DependencyInjection;

namespace Echo.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<IPerformanceTraceExporter, PerformanceTraceExporter>();
        services.AddSingleton<IPerformanceTrace, PerformanceTraceService>();
        services.AddSingleton<IPerformanceTraceStore, PerformanceTraceStore>();
        services.AddSingleton<IProcessTelemetryReader, ProcessTelemetryReader>();

        return services;
    }
}
