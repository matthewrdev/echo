namespace Echo.Meshing;

internal sealed class BinaryCut
{
    private readonly int[] heads;
    private readonly int[] levels;
    private readonly int[] current;
    private readonly int[] queue;
    private readonly int[] path;
    private readonly double[] bottlenecks;
    private int[] destinations;
    private int[] next;
    private double[] capacities;
    private int arcCount;
    private int nodeCount;

    public BinaryCut(int nodes)
    {
        heads = new int[nodes + 2];
        Array.Fill(heads, -1);
        levels = new int[heads.Length];
        current = new int[heads.Length];
        queue = new int[heads.Length];
        path = new int[heads.Length];
        bottlenecks = new double[heads.Length];
        destinations = new int[Math.Max(16, checked(nodes * 7))];
        next = new int[destinations.Length];
        capacities = new double[destinations.Length];
        Reset(nodes);
    }

    public void Reset(int nodes)
    {
        if (nodes < 0 || nodes > heads.Length - 2)
        {
            throw new ArgumentOutOfRangeException(nameof(nodes));
        }
        nodeCount = nodes + 2;
        arcCount = 0;
        Array.Fill(heads, -1, 0, nodeCount);
    }

    public void Unary(int node, double keep, double change)
    {
        var minimum = Math.Min(keep, change);
        Pair(nodeCount - 2, node, change - minimum, 0);
        Pair(node, nodeCount - 1, keep - minimum, 0);
    }

    public void Pair(int first, int second, double forward, double reverse)
    {
        if (arcCount + 2 > destinations.Length)
        {
            Array.Resize(ref destinations, destinations.Length * 2);
            Array.Resize(ref next, destinations.Length);
            Array.Resize(ref capacities, destinations.Length);
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

    public void Solve(Span<bool> expansion, CancellationToken cancellation)
    {
        var source = nodeCount - 2;
        var sink = nodeCount - 1;
        if (expansion.Length != source)
        {
            throw new ArgumentException("Expected one output per active node.", nameof(expansion));
        }
        while (Levels(source, sink, cancellation))
        {
            heads.AsSpan(0, nodeCount).CopyTo(current);
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
        for (var index = 0; index < expansion.Length; index++)
        {
            expansion[index] = levels[index] < 0;
        }
    }

    private bool Levels(int source, int sink, CancellationToken cancellation)
    {
        Array.Fill(levels, -1, 0, nodeCount);
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
