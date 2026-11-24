using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Echo.Logging;

public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddLogging(this IServiceCollection services)
    {
        var configured = Environment.GetEnvironmentVariable("ECHO_LOG_LEVEL");
        var level = Enum.TryParse<LogLevel>(configured, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : LogLevel.None;

        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(level);
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.ColorBehavior = LoggerColorBehavior.Disabled;
            });
            logging.Services.Configure<ConsoleLoggerOptions>(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
        });

        return services;
    }
}
