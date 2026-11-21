using System.Globalization;
using Echo.Cli;
using Echo.Composition;
using Microsoft.Extensions.DependencyInjection;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
var services = new ServiceCollection();
new ProcessingServices().Register(services);
new CliServices().Register(services);
using var provider = services.BuildServiceProvider();

return provider.GetRequiredService<ICommandApplication>().Run(args);
