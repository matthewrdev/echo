using Echo.Projects;

namespace Echo.Pipeline.Runs;

public interface IRunStateRecovery
{
    void Recover(IProjectStore store);
}
