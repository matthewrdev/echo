namespace Echo.Storage;

internal sealed class WorkerMemoryRegistration : IDisposable
{
    private readonly ProcessingMemoryTracker owner;
    private readonly int id;

    public WorkerMemoryRegistration(ProcessingMemoryTracker owner, int id)
    {
        this.owner = owner;
        this.id = id;
    }

    public void Dispose() => owner.Remove(id);
}
