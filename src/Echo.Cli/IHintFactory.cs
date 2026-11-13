using Echo.Contracts;

namespace Echo.Cli;

public interface IHintFactory
{
    Hint Create(Arguments args, string action);
}
