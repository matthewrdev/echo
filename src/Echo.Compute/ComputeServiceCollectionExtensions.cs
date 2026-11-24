using Microsoft.Extensions.DependencyInjection;

namespace Echo.Compute;

public static class ComputeServiceCollectionExtensions
{
    public static IServiceCollection AddCompute(this IServiceCollection services)
    {
        services.AddSingleton<IComputeService, ComputeService>();

        return services;
    }
}
