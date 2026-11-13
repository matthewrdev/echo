using Echo.Contracts;

namespace Echo.Reconstruction;

public interface IAlignmentRecoveryEvaluator
{
    AlignmentRecoveryAssessment Evaluate(ReconstructionScene established, ReconstructionScene proposed);
}
