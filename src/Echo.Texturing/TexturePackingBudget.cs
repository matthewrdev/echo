namespace Echo.Texturing;

internal sealed class TexturePackingBudget
{
    private long remaining;

    public TexturePackingBudget(long maximumCandidates)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCandidates);
        remaining = maximumCandidates;
    }

    public bool Exhausted
    {
        get; private set;
    }

    public bool TrySpend(long candidates)
    {
        if (candidates > remaining)
        {
            Exhausted = true;
            return false;
        }
        remaining -= candidates;
        return true;
    }
}
