using Echo.Depth;
using Echo.Models;
using Echo.Pipeline.Runs;

namespace Echo.Pipeline.Artifacts;

public interface ICliffRecoveryArtifacts
{
    string[] Build(RunContext context, ReconstructionScene scene, string[] originalUnits, IReadOnlyDictionary<string, SemanticMap> semantics, SubjectDistancePlan? subjectDistance = null);
}
