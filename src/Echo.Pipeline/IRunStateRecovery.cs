using Echo.Projects;

namespace Echo.Pipeline;

public interface IRunStateRecovery
{
    void Recover(IProjectStore store);
}
