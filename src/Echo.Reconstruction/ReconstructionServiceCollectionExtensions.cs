using Microsoft.Extensions.DependencyInjection;

namespace Echo.Reconstruction;

public static class ReconstructionServiceCollectionExtensions
{
    public static IServiceCollection AddReconstruction(this IServiceCollection services)
    {
        services.AddSingleton<IReconstructor, Reconstructor>();
        services.AddSingleton<ISurveyControl, SurveyControl>();
        services.AddSingleton<IVisualReconstructor, VisualReconstructor>();

        return services;
    }
}
