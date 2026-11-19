using Echo.Projects;

namespace Echo.Validation;

public interface IValidationInputs
{
    IProjectStore Prepare(ReconstructionCase fixture, string assets, string output, CancellationToken cancellation);
}
