using Echo.Contracts;
using Echo.Depth;

namespace Echo.Pipeline;

public interface ICliffRecoveryArtifacts
{
    string[] Build(RunContext context, ReconstructionScene scene, string[] originalUnits, IReadOnlyDictionary<string, SemanticMap> semantics, SubjectDistancePlan? subjectDistance = null);
}
