namespace Echo.Projects;

public sealed record PhotoDeduplicationPlan(int Revision, int EnabledBefore, IReadOnlyList<PhotoDuplicateGroup> Groups)
{
    public int DuplicateCount => Groups.Sum(group => group.Duplicates.Count);
    public int EnabledAfter => EnabledBefore - DuplicateCount;
}
