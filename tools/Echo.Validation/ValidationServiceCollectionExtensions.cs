using Echo.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Validation;

public static class ValidationServiceCollectionExtensions
{
    public static IServiceCollection AddValidation(this IServiceCollection services)
    {
        services.AddSingleton<IReconstructionMeasurements, ReconstructionMeasurements>();
        services.AddSingleton<ValidationApplication>();
        services.AddSingleton<IValidationInputs, ValidationInputs>();
        services.AddSingleton<IJsonReadMetadata, ValidationJsonMetadata>();

        return services;
    }
}
