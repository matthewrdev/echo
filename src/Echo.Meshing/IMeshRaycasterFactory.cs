using Echo.Models;

namespace Echo.Meshing;

public interface IMeshRaycasterFactory
{
    IMeshRaycaster Create(Surface surface, CancellationToken cancellation);
}
