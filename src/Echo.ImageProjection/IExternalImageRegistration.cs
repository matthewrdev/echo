namespace Echo.ImageProjection;

internal interface IExternalImageRegistration
{
    RegistrationResult Register(ExternalRegistrationRequest request, CancellationToken cancellation);
}
