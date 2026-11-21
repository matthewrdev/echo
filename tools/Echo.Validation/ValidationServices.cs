using Echo.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Validation;

public sealed class ValidationServices
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IReconstructionMeasurements, ReconstructionMeasurements>();
        services.AddSingleton<ValidationApplication>();
        services.AddSingleton<IValidationInputs, ValidationInputs>();
        services.AddSingleton<IJsonReadMetadata, ValidationJsonMetadata>();
    }
}
