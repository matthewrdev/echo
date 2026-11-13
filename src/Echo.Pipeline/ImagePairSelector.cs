using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Pipeline;

[Export(typeof(IImagePairSelector)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ImagePairSelector : IImagePairSelector
{
    private readonly ICameraGeometry geometry;
    private readonly IFrustumOverlap overlap;

    [ImportingConstructor]
    public ImagePairSelector(ICameraGeometry geometry, IFrustumOverlap overlap)
    {
        this.geometry = geometry;
        this.overlap = overlap;
    }

    public IReadOnlyList<PairIdentity> Select(ProjectData project, IReadOnlyList<Photo> images, IReadOnlyDictionary<string, string> featureUnits)
    {
        var wanted = new Dictionary<string, PairIdentity>();
        void AddPair(string leftImageId, string rightImageId)
        {
            if (string.CompareOrdinal(leftImageId, rightImageId) > 0)
            {
                var temporary = leftImageId;
                leftImageId = rightImageId;
                rightImageId = temporary;
            }

            var availablePair = leftImageId != rightImageId
                && featureUnits.ContainsKey(leftImageId)
                && featureUnits.ContainsKey(rightImageId);
            if (availablePair)
            {
                wanted.TryAdd(leftImageId + ":" + rightImageId, new PairIdentity
                {
                    A = leftImageId,
                    B = rightImageId
                });
            }
        }

        for (var imageIndex = 0; imageIndex < images.Count; imageIndex++)
        {
            var end = project.Settings.MatchAllPairs ? images.Count : Math.Min(images.Count, imageIndex + 1 + project.Settings.PairWindow);
            for (var otherIndex = imageIndex + 1; otherIndex < end; otherIndex++)
            {
                AddPair(images[imageIndex].Id, images[otherIndex].Id);
            }
        }

        if (!project.Settings.MatchAllPairs)
        {
            var priors = CapturePriors(project, images);
            var candidates = new List<CapturePairCandidate>(images.Count);
            var selected = new HashSet<int>();
            for (var imageIndex = 0; imageIndex < images.Count; imageIndex++)
            {
                var source = priors[imageIndex];
                if (source.Position == null)
                {
                    continue;
                }
                candidates.Clear();
                for (var otherIndex = 0; otherIndex < images.Count; otherIndex++)
                {
                    var target = priors[otherIndex];
                    var available = otherIndex != imageIndex && target.Position != null
                        && featureUnits.ContainsKey(images[otherIndex].Id);
                    if (!available)
                    {
                        continue;
                    }
                    var sameDatum = source.Position.AltitudeDatum == target.Position!.AltitudeDatum;
                    var comparableAltitude = sameDatum && source.Position.Altitude.HasValue && target.Position.Altitude.HasValue;
                    var delta = source.Centre - target.Centre;
                    var distanceSquared = delta.X * delta.X + delta.Z * delta.Z + (comparableAltitude ? delta.Y * delta.Y : 0);
                    var bothViewsAvailable = source.Frustum != null && target.Frustum != null;
                    var canCompareViews = comparableAltitude && bothViewsAvailable && project.Settings.FrustumPairNeighbours > 0;
                    var score = canCompareViews ? overlap.Score(source.Frustum!, target.Frustum!) : 0;
                    var differentElevation = comparableAltitude && Math.Abs(delta.Y) >= 3;
                    var differentTilt = source.Attitude != null && target.Attitude != null
                        && Math.Abs(source.Attitude.Pitch - target.Attitude.Pitch) >= 8;
                    candidates.Add(new CapturePairCandidate(otherIndex, images[otherIndex].Id, distanceSquared, score,
                        differentElevation || differentTilt));
                }

                candidates.Sort(CompareDistance);
                for (var index = 0; index < Math.Min(project.Settings.GpsPairNeighbours, candidates.Count); index++)
                {
                    AddPair(images[imageIndex].Id, candidates[index].ImageId);
                }

                candidates.Sort(CompareOverlap);
                selected.Clear();
                var budget = project.Settings.FrustumPairNeighbours;
                // Reserve half the additional comparisons for another elevation or tilt.
                // Otherwise a dense flight pass can consume the entire overlap shortlist.
                var crossPassBudget = (budget + 1) / 2;
                foreach (var candidate in candidates)
                {
                    var crossPass = candidate.OverlapScore > 0 && candidate.DifferentElevationOrTilt
                        && selected.Count < crossPassBudget;
                    if (crossPass)
                    {
                        selected.Add(candidate.ImageIndex);
                        AddPair(images[imageIndex].Id, candidate.ImageId);
                    }
                }
                foreach (var candidate in candidates)
                {
                    if (selected.Count >= budget || candidate.OverlapScore <= 0)
                    {
                        break;
                    }
                    if (selected.Add(candidate.ImageIndex))
                    {
                        AddPair(images[imageIndex].Id, candidate.ImageId);
                    }
                }
            }
        }

        foreach (var hint in project.Hints)
        {
            var requestedPair = hint.Enabled && hint.Type is "overlap" or "tie" && hint.OtherImageId != null;
            if (requestedPair)
            {
                AddPair(hint.ImageId, hint.OtherImageId!);
            }
        }

        var result = new PairIdentity[wanted.Count];
        wanted.Values.CopyTo(result, 0);
        return result;
    }

    private CapturePrior[] CapturePriors(ProjectData project, IReadOnlyList<Photo> images)
    {
        var hints = new Dictionary<string, CapturePrior>(StringComparer.Ordinal);
        foreach (var photo in images)
        {
            hints[photo.Id] = new CapturePrior(photo.Gps, photo.Attitude, default, null);
        }
        foreach (var hint in project.Hints)
        {
            var cameraHint = hint.Enabled && hint.Type == "camera";
            if (cameraHint && hints.TryGetValue(hint.ImageId, out var prior))
            {
                hints[hint.ImageId] = new CapturePrior(hint.Position ?? prior.Position,
                    hint.Attitude ?? prior.Attitude, default, null);
            }
        }
        var priors = new CapturePrior[images.Count];
        GeoPosition? origin = null;
        for (var index = 0; index < images.Count; index++)
        {
            var photo = images[index];
            var prior = hints[photo.Id];
            var position = prior.Position;
            var attitude = prior.Attitude;
            origin ??= position;
            var centre = position == null ? default : geometry.ToLocal(position, origin!);
            CaptureFrustum? frustum = null;
            var located = position?.Altitude != null && attitude != null;
            var validIntrinsics = photo.Width > 0 && photo.Height > 0 && photo.FocalPixels > 0;
            if (located && validIntrinsics)
            {
                frustum = new CaptureFrustum(centre, geometry.AttitudeRotation(attitude!),
                    photo.Width / (2 * photo.FocalPixels), photo.Height / (2 * photo.FocalPixels),
                    project.Settings.PairSearchDistance, position!.UncertaintyMetres);
            }
            priors[index] = new CapturePrior(position, attitude, centre, frustum);
        }
        return priors;
    }

    private int CompareDistance(CapturePairCandidate left, CapturePairCandidate right)
    {
        var distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
        return distance != 0 ? distance : string.CompareOrdinal(left.ImageId, right.ImageId);
    }

    private int CompareOverlap(CapturePairCandidate left, CapturePairCandidate right)
    {
        var score = right.OverlapScore.CompareTo(left.OverlapScore);
        return score != 0 ? score : CompareDistance(left, right);
    }
}
