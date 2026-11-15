using Echo.Contracts;

namespace Echo.Pipeline;

public interface ISceneSemanticArtifacts
{
    IReadOnlyDictionary<string, SemanticMap> Build(RunContext context);
}
