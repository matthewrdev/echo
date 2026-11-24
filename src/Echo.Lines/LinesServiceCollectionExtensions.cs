using Microsoft.Extensions.DependencyInjection;

namespace Echo.Lines;

public static class LinesServiceCollectionExtensions
{
    public static IServiceCollection AddLines(this IServiceCollection services)
    {
        services.AddSingleton<ILineDetector, LineDetector>();

        return services;
    }
}
