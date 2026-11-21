using Echo.Composition;
using Echo.Validation;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
new ProcessingServices().Register(services);
new ValidationServices().Register(services);
using var provider = services.BuildServiceProvider();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    return provider.GetRequiredService<ValidationApplication>().Run(args, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Validation cancelled; saved work and reports are retained.");
    return 130;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
