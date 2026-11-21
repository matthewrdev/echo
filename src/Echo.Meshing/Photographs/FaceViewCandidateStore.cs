using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Photographs;

internal sealed class FaceViewCandidateStore
{
    private readonly IList<int> offsets;
    private readonly IList<int> counts;
    private readonly IList<FaceViewCandidate> entries;

    public FaceViewCandidateStore(int faces, WorkBuffers? work, CancellationToken cancellation)
    {
        offsets = work?.Filled(faces, 0, new IntRecordCodec(), cancellation) ?? new int[faces];
        counts = work?.Filled(faces, 0, new IntRecordCodec(), cancellation) ?? new int[faces];
        entries = work?.List(new FaceViewCandidateCodec()) ?? new List<FaceViewCandidate>();
    }

    public int FaceCount => counts.Count;
    public int Count(int face) => counts[face];
    public FaceViewCandidate Get(int face, int option) => entries[offsets[face] + option];

    public void Set(int face, List<FaceViewCandidate> options)
    {
        offsets[face] = entries.Count;
        counts[face] = options.Count;
        foreach (var option in options)
        {
            entries.Add(option);
        }
    }
}
