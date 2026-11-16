using Echo.Storage;

namespace Echo.Fusion;

// A small FIFO of known-present coordinates avoids repeating SQLite lookups for
// neighbouring depth pixels. Eviction only costs a lookup; it never drops a cell.
internal sealed class GridCandidateAccumulator
{
    private readonly IDictionary<Grid, int> cells;
    private readonly HashSet<Grid> recent;
    private readonly Grid[] order;
    private int next;

    public GridCandidateAccumulator(IDictionary<Grid, int> cells, WorkBuffers? work)
    {
        this.cells = cells;
        var capacity = work == null ? 0 : (int)(work.ReserveIndexMemory(4 * 1024 * 1024) / 128);
        recent = new HashSet<Grid>(capacity);
        order = new Grid[capacity];
    }

    public void Add(Grid cell)
    {
        if (recent.Contains(cell))
        {
            return;
        }
        if (order.Length > 0)
        {
            if (recent.Count == order.Length)
            {
                recent.Remove(order[next]);
            }
            recent.Add(cell);
            order[next] = cell;
            next = (next + 1) % order.Length;
        }
        switch (cells)
        {
            case SpillDictionary<Grid, int> spill:
                spill.TryAdd(cell, 0);
                break;
            case DiskDictionary<Grid, int> disk:
                disk.TryAdd(cell, 0);
                break;
            default:
                cells.TryAdd(cell, 0);
                break;
        }
    }
}
