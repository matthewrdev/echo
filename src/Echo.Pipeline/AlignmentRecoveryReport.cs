using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class AlignmentRecoveryReport
{
    public required string Initial
    {
        get; init;
    }
    public required List<string> RecoveryUnits
    {
        get; init;
    }
    public required int RecoveredPairs
    {
        get; init;
    }
    public required AlignmentRecoveryAssessment? Assessment
    {
        get; init;
    }
}
