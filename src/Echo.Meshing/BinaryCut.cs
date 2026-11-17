using Echo.Storage;

namespace Echo.Meshing;

internal sealed class BinaryCut
{
    private readonly IList<int> heads;
    private readonly IList<int> levels;
    private readonly IList<int> current;
    private readonly IList<int> queue;
    private readonly IList<int> path;
    private readonly IList<double> bottlenecks;
    private readonly IList<int> destinations;
    private readonly IList<int> next;
    private readonly IList<double> capacities;
    private int arcCount;
    private int nodeCount;

    public BinaryCut(int nodes, WorkBuffers? work = null, CancellationToken cancellation = default)
    {
        heads = work?.Filled(nodes + 2, -1, new IntRecordCodec(), cancellation) ?? Enumerable.Repeat(-1, nodes + 2).ToArray();
        levels = work?.Filled(nodes + 2, 0, new IntRecordCodec(), cancellation) ?? new int[nodes + 2];
        current = work?.Filled(nodes + 2, 0, new IntRecordCodec(), cancellation) ?? new int[nodes + 2];
        queue = work?.Filled(nodes + 2, 0, new IntRecordCodec(), cancellation) ?? new int[nodes + 2];
        path = work?.Filled(nodes + 2, 0, new IntRecordCodec(), cancellation) ?? new int[nodes + 2];
        bottlenecks = work?.Filled(nodes + 2, 0d, new DoubleRecordCodec(), cancellation) ?? new double[nodes + 2];
        destinations = work?.List(new IntRecordCodec()) ?? new List<int>();
        next = work?.List(new IntRecordCodec()) ?? new List<int>();
        capacities = work?.List(new DoubleRecordCodec()) ?? new List<double>();
        Reset(nodes);
    }

    public void Reset(int nodes)
    {
        if (nodes < 0 || nodes > heads.Count - 2)
        {
            throw new ArgumentOutOfRangeException(nameof(nodes));
        }
        nodeCount = nodes + 2;
        arcCount = 0;
        RecordListOperations.Fill(heads, nodeCount, -1);
    }

    public void Unary(int node, double keep, double change)
    {
        var minimum = Math.Min(keep, change);
        Pair(nodeCount - 2, node, change - minimum, 0);
        Pair(node, nodeCount - 1, keep - minimum, 0);
    }

    public void Pair(int first, int second, double forward, double reverse)
    {
        while (arcCount + 2 > destinations.Count)
        {
            destinations.Add(0);
            next.Add(0);
            capacities.Add(0);
        }
        destinations[arcCount] = second;
        capacities[arcCount] = forward;
        next[arcCount] = heads[first];
        heads[first] = arcCount++;
        destinations[arcCount] = first;
        capacities[arcCount] = reverse;
        next[arcCount] = heads[second];
        heads[second] = arcCount++;
    }

    public void Solve(IList<bool> expansion, CancellationToken cancellation)
    {
        var source = nodeCount - 2;
        var sink = nodeCount - 1;
        if (expansion.Count < source)
        {
            throw new ArgumentException("Expected one output per active node.", nameof(expansion));
        }
        while (Levels(source, sink, cancellation))
        {
            RecordListOperations.Copy(heads, current, nodeCount);
            var depth = 0;
            var node = source;
            bottlenecks[0] = double.PositiveInfinity;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (node == sink)
                {
                    var flow = bottlenecks[depth];
                    var restart = depth;
                    for (var index = 0; index < depth; index++)
                    {
                        var arc = path[index];
                        capacities[arc] -= flow;
                        capacities[arc ^ 1] += flow;
                        if (capacities[arc] <= 1e-10)
                        {
                            restart = Math.Min(restart, index);
                        }
                    }
                    depth = restart;
                    node = depth == 0 ? source : destinations[path[depth - 1]];
                    for (var index = 0; index < depth; index++)
                    {
                        bottlenecks[index + 1] = Math.Min(bottlenecks[index], capacities[path[index]]);
                    }
                    continue;
                }

                var edge = current[node];
                while (edge >= 0)
                {
                    if (capacities[edge] > 1e-10 && levels[destinations[edge]] == levels[node] + 1)
                    {
                        break;
                    }
                    edge = next[edge];
                }
                current[node] = edge;
                if (edge < 0)
                {
                    levels[node] = -1;
                    if (depth == 0)
                    {
                        break;
                    }
                    depth--;
                    node = depth == 0 ? source : destinations[path[depth - 1]];
                    current[node] = next[path[depth]];
                    continue;
                }
                path[depth] = edge;
                bottlenecks[depth + 1] = Math.Min(bottlenecks[depth], capacities[edge]);
                node = destinations[edge];
                depth++;
            }
        }
        for (var index = 0; index < source; index++)
        {
            expansion[index] = levels[index] < 0;
        }
    }

    private bool Levels(int source, int sink, CancellationToken cancellation)
    {
        RecordListOperations.Fill(levels, nodeCount, -1);
        levels[source] = 0;
        queue[0] = source;
        var count = 1;
        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var node = queue[index];
            for (var arc = heads[node]; arc >= 0; arc = next[arc])
            {
                var destination = destinations[arc];
                if (capacities[arc] > 1e-10 && levels[destination] < 0)
                {
                    levels[destination] = levels[node] + 1;
                    queue[count++] = destination;
                }
            }
        }
        return levels[sink] >= 0;
    }
}
