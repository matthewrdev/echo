using Echo.Storage;

namespace Echo.Meshing;

internal static class FaceViewOptimizer
{
    public static void Optimize(FaceViewCandidateStore candidates, int[] selected, IList<int> neighbours,
        IList<double> penalties, int viewCount, CancellationToken cancellation, Action<string>? progress = null, WorkBuffers? work = null)
    {
        using var scratch = work?.CreateScope(preferResident: true);
        // Exact contiguous view ranges avoid growing one List for every camera.
        var offsets = new int[viewCount + 1];
        for (var face = 0; face < candidates.FaceCount; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var option = 0; option < candidates.Count(face); option++)
            {
                offsets[candidates.Get(face, option).View + 1]++;
            }
        }
        var maximumActive = 0;
        for (var view = 0; view < viewCount; view++)
        {
            maximumActive = Math.Max(maximumActive, offsets[view + 1]);
            offsets[view + 1] = checked(offsets[view + 1] + offsets[view]);
        }
        IList<FaceViewChoice> available = scratch?.Filled(offsets[^1], default(FaceViewChoice), new FaceViewChoiceCodec(), cancellation) ?? new FaceViewChoice[offsets[^1]];
        var writeOffsets = (int[])offsets.Clone();
        for (var face = 0; face < candidates.FaceCount; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var option = 0; option < candidates.Count(face); option++)
            {
                available[writeOffsets[candidates.Get(face, option).View]++] = new FaceViewChoice(face, option);
            }
        }
        IList<int> active = scratch?.Filled(maximumActive, 0, new IntRecordCodec(), cancellation) ?? new int[maximumActive];
        IList<double> keep = scratch?.Filled(maximumActive, 0d, new DoubleRecordCodec(), cancellation) ?? new double[maximumActive];
        IList<double> change = scratch?.Filled(maximumActive, 0d, new DoubleRecordCodec(), cancellation) ?? new double[maximumActive];
        IList<double> viewCosts = scratch?.Filled(maximumActive, 0d, new DoubleRecordCodec(), cancellation) ?? new double[maximumActive];
        IList<double> selectedCosts = scratch?.Filled(selected.Length, 0d, new DoubleRecordCodec(), cancellation) ?? new double[selected.Length];
        IList<bool> expansion = scratch?.Filled(maximumActive, false, new BoolRecordCodec(), cancellation) ?? new bool[maximumActive];
        BinaryCut? cut = null;
        IList<int> activeIndex = scratch?.Filled(selected.Length, -1, new IntRecordCodec(), cancellation) ?? Enumerable.Repeat(-1, selected.Length).ToArray();
        for (var face = 0; face < selected.Length; face++)
        {
            if (selected[face] >= 0)
            {
                selectedCosts[face] = Cost(candidates, face, selected[face]);
            }
        }
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
                var count = 0;
                for (var slot = offsets[view]; slot < offsets[view + 1]; slot++)
                {
                    var choice = available[slot];
                    if (selected[choice.Face] != view)
                    {
                        active[count] = choice.Face;
                        viewCosts[count++] = candidates.Get(choice.Face, choice.Option).Cost;
                    }
                }
                if (count == 0)
                {
                    continue;
                }
                for (var index = 0; index < count; index++)
                {
                    activeIndex[active[index]] = index;
                }
                RecordListOperations.Fill(keep, count, 0d);
                RecordListOperations.Fill(change, count, 0d);
                cut ??= new BinaryCut(maximumActive, scratch, cancellation);
                cut.Reset(count);
                for (var index = 0; index < count; index++)
                {
                    var face = active[index];
                    keep[index] += selectedCosts[face];
                    change[index] += viewCosts[index];
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
                for (var index = 0; index < count; index++)
                {
                    cut.Unary(index, keep[index], change[index]);
                }
                cut.Solve(expansion, cancellation);
                var improvement = 0.0;
                for (var index = 0; index < count; index++)
                {
                    if (!expansion[index])
                    {
                        continue;
                    }
                    var face = active[index];
                    improvement += viewCosts[index] - selectedCosts[face];
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
                    for (var index = 0; index < count; index++)
                    {
                        if (expansion[index])
                        {
                            selected[active[index]] = view;
                            selectedCosts[active[index]] = viewCosts[index];
                            changes++;
                        }
                    }
                }
                for (var index = 0; index < count; index++)
                {
                    activeIndex[active[index]] = -1;
                }
            }
            if (changes == 0)
            {
                break;
            }
        }
    }

    private static double Cost(FaceViewCandidateStore candidates, int face, int view)
    {
        for (var option = 0; option < candidates.Count(face); option++)
        {
            var candidate = candidates.Get(face, option);
            if (candidate.View == view)
            {
                return candidate.Cost;
            }
        }
        throw new InvalidOperationException("A texture expansion selected an unavailable view.");
    }
}
