using Echo.Models;
using Echo.Pipeline.Runs;

namespace Echo.Pipeline.Semantics;

public interface ISceneSemanticArtifacts
{
    IReadOnlyDictionary<string, SemanticMap> Build(RunContext context, string progressStage = "depth");
}
