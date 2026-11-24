using Echo.Models;

namespace Echo.Cli.Commands.Images;

internal sealed record DroneScanGroup(string Name, PhotoImportEntry[] Photos);
