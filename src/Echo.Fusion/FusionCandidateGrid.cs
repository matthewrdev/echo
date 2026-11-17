using System.Collections;

namespace Echo.Fusion;

public sealed class FusionCandidateGrid : IEnumerable<Grid>
{
    private readonly IList<GridSpan> spans;
    private readonly GridAxis axis;

    public FusionCandidateGrid(IList<GridSpan> spans, long count, GridAxis axis)
    {
        this.spans = spans;
        Count = count;
        this.axis = axis;
    }

    public long Count
    {
        get;
    }

    public IEnumerator<Grid> GetEnumerator()
    {
        foreach (var span in spans)
        {
            for (long z = span.FirstZ; z <= span.LastZ; z++)
            {
                yield return FusionGridAxes.ToWorld(new Grid(span.X, span.Y, (int)z), axis);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IList<GridSpan> Spans => spans;
    public GridAxis Axis => axis;

    internal FusionGridReader OpenReader() => new(spans, axis);
}
