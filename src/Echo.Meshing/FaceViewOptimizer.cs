namespace Echo.Meshing;

internal static class FaceViewOptimizer
{
    public static void Optimize(FaceViewCandidate[][] candidates, int[] selected, int[] neighbours,
        double[] penalties, int viewCount, CancellationToken cancellation, Action<string>? progress = null)
    {
        var available = Enumerable.Range(0, viewCount).Select(_ => new List<int>()).ToArray();
        for (var face = 0; face < candidates.Length; face++)
        {
            foreach (var candidate in candidates[face])
            {
                available[candidate.View].Add(face);
            }
        }
        var activeIndex = new int[selected.Length];
        Array.Fill(activeIndex, -1);
        // Alpha expansion solves a binary cut for an entire region at a time.
        // Candidate visibility is a hard constraint. Every accepted expansion
        // decreases the same unary-quality plus pairwise-seam energy.
        for (var pass = 0; pass < 4; pass++)
        {
            var changes = 0;
            for (var step = 0; step < viewCount; step++)
            {
                cancellation.ThrowIfCancellationRequested();
                var view = pass % 2 == 0 ? step : viewCount - step - 1;
                progress?.Invoke($"Choosing coherent source photographs: pass {pass + 1}/4, view {step + 1}/{viewCount}…");
                var active = available[view].Where(face => selected[face] != view).ToArray();
                if (active.Length == 0)
                {
                    continue;
                }
                for (var index = 0; index < active.Length; index++)
                {
                    activeIndex[active[index]] = index;
                }
                var keep = new double[active.Length];
                var change = new double[active.Length];
                var cut = new BinaryCut(active.Length);
                for (var index = 0; index < active.Length; index++)
                {
                    var face = active[index];
                    keep[index] += Cost(candidates[face], selected[face]);
                    change[index] += Cost(candidates[face], view);
                    for (var corner = 0; corner < 3; corner++)
                    {
                        var slot = face * 3 + corner;
                        var other = neighbours[slot];
                        if (other < 0 || selected[other] < 0)
                        {
                            continue;
                        }
                        var penalty = penalties[slot];
                        var otherIndex = activeIndex[other];
                        if (otherIndex < 0)
                        {
                            keep[index] += selected[face] == selected[other] ? 0 : penalty;
                            change[index] += view == selected[other] ? 0 : penalty;
                        }
                        else if (index < otherIndex)
                        {
                            var different = selected[face] != selected[other];
                            var capacity = different ? penalty * .5 : penalty;
                            if (different)
                            {
                                change[index] -= capacity;
                                change[otherIndex] -= capacity;
                            }
                            cut.Pair(index, otherIndex, capacity, capacity);
                        }
                    }
                }
                for (var index = 0; index < active.Length; index++)
                {
                    cut.Unary(index, keep[index], change[index]);
                }
                var expansion = cut.Solve(cancellation);
                var improvement = 0.0;
                for (var index = 0; index < active.Length; index++)
                {
                    if (!expansion[index])
                    {
                        continue;
                    }
                    var face = active[index];
                    improvement += Cost(candidates[face], view) - Cost(candidates[face], selected[face]);
                    for (var corner = 0; corner < 3; corner++)
                    {
                        var slot = face * 3 + corner;
                        var other = neighbours[slot];
                        if (other < 0 || selected[other] < 0)
                        {
                            continue;
                        }
                        var otherIndex = activeIndex[other];
                        var otherChanges = otherIndex >= 0 && expansion[otherIndex];
                        if (otherChanges && index > otherIndex)
                        {
                            continue;
                        }
                        var before = selected[face] == selected[other] ? 0 : penalties[slot];
                        var after = otherChanges || view == selected[other] ? 0 : penalties[slot];
                        improvement += after - before;
                    }
                }
                if (improvement < -1e-8)
                {
                    for (var index = 0; index < active.Length; index++)
                    {
                        if (expansion[index])
                        {
                            selected[active[index]] = view;
                            changes++;
                        }
                    }
                }
                foreach (var face in active)
                {
                    activeIndex[face] = -1;
                }
            }
            if (changes == 0)
            {
                break;
            }
        }
    }

    private static double Cost(FaceViewCandidate[] candidates, int view)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.View == view)
            {
                return candidate.Cost;
            }
        }
        throw new InvalidOperationException("A texture expansion selected an unavailable view.");
    }
}
