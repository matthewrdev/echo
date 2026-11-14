using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Reconstruction;

public static class AlignmentRecoveryEvaluator
{
    public static AlignmentRecoveryAssessment Evaluate(ReconstructionScene established, ReconstructionScene proposed)
    {
        var previous = Observations(established);
        var candidate = Observations(proposed);
        var retained = 0;
        foreach (var observation in previous)
        {
            if (candidate.Contains(observation))
            {
                retained++;
            }
        }
        // A recovery pass must preserve established multi-view support. A higher raw
        // match count cannot justify silently throwing away a large part of the model.
        var preservesSupport = previous.Count > 0 && retained >= previous.Count * .95;
        var addsSupport = candidate.Count > previous.Count;
        var accepted = preservesSupport && addsSupport;
        return new AlignmentRecoveryAssessment
        {
            Accepted = accepted,
            EstablishedObservations = previous.Count,
            RetainedObservations = retained,
            ProposedObservations = candidate.Count,
            Reason = accepted
                ? "Second pass retained at least 95% of established observations and added multi-view support."
                : "Second pass did not preserve and expand established multi-view support. Kept the initial verified alignment; cross-pass recovery remains unresolved."
        };
    }

    private static HashSet<ImageObservationKey> Observations(ReconstructionScene scene)
    {
        var result = new HashSet<ImageObservationKey>();
        foreach (var point in scene.Points)
        {
            result.Add(Key(point.ImageA, point.PixelA));
            result.Add(Key(point.ImageB, point.PixelB));
        }
        return result;
    }

    private static ImageObservationKey Key(string image, double[] pixel)
    {
        return new ImageObservationKey(image, (long)Math.Round(pixel[0] * 10), (long)Math.Round(pixel[1] * 10));
    }
}
