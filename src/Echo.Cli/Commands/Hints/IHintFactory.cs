using Echo.Models;

namespace Echo.Cli.Commands.Hints;

public interface IHintFactory
{
    Hint Create(Arguments args, string action);
}
