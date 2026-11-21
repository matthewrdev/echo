using Echo.Models;

namespace Echo.Cli;

public interface IHintFactory
{
    Hint Create(Arguments args, string action);
}
