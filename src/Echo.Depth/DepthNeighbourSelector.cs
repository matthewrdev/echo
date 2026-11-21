using Echo.Models;
using Echo.Geometry;

namespace Echo.Depth;

public static class DepthNeighbourSelector
{
    public static int MaximumNeighbours(int minimumNeighbours, int cameraCount)
    {
        return minimumNeighbours <= 0 ? 0 : Math.Min(Math.Max(0, cameraCount - 1), Math.Clamp(minimumNeighbours, 12, 32));
    }

    public static CameraPose[] Select(ProjectData project, ReconstructionScene scene, CameraPose reference,
        DepthSearchRange range, int minimumNeighbours, CancellationToken cancellation,
        DepthMap? prior = null, IReadOnlyList<CameraPose>? previous = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var capacity = MaximumNeighbours(minimumNeighbours, scene.Cameras.Count);
        if (capacity == 0)
        {
            return [];
        }

        var photos = project.Images.ToDictionary(photo => photo.Id);
        var samples = DepthCoverageGrid.Create(scene, photos[reference.ImageId], reference, range, prior, cancellation);
        var candidates = Candidates(project, scene, reference, photos, samples, cancellation);
        var selected = new List<DepthNeighbourCandidate>(capacity);
        var support = new int[samples.Length];
        if (previous != null)
        {
            // Retain loaded sources between levels: refinement can add coverage,
            // but the lifetime union must remain within the admission budget.
            foreach (var camera in previous)
            {
                var candidate = candidates.Find(item => item.Camera.ImageId == camera.ImageId);
                if (candidate != null && selected.Count < capacity)
                {
                    Add(selected, support, candidate, range.Median);
                }
            }
        }

        if (selected.Count == 0)
        {
            SeedNearby(candidates, selected, support, Math.Min(minimumNeighbours, capacity), range.Median);
        }

        while (selected.Count < capacity)
        {
            cancellation.ThrowIfCancellationRequested();
            DepthNeighbourCandidate? best = null;
            var bestScore = 0.0;
            foreach (var candidate in candidates)
            {
                if (Redundant(candidate, selected, range.Median))
                {
                    continue;
                }

                var score = Score(candidate, samples, support, selected.Count < minimumNeighbours);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best == null)
            {
                break;
            }

            Add(selected, support, best, range.Median);
        }

        return selected.Select(item => item.Camera).ToArray();
    }

    private static void SeedNearby(List<DepthNeighbourCandidate> candidates, List<DepthNeighbourCandidate> selected,
        int[] support, int minimumNeighbours, double medianDepth)
    {
        var minimumBaseline = Math.Clamp(medianDepth * .06, .75, 3);
        var usable = candidates.Where(candidate => candidate.Quality.Any(value => value > 0)).OrderBy(candidate => candidate.Distance).ToArray();
        var preferred = usable.Count(candidate => candidate.Distance >= minimumBaseline) >= 2;
        foreach (var candidate in usable)
        {
            var shortBaseline = preferred && candidate.Distance < minimumBaseline;
            if (shortBaseline)
            {
                continue;
            }
            // Preserve the configured nearby photographic matches. Coincident
            // sources still count as only one viewpoint when planning coverage.
            Add(selected, support, candidate, medianDepth);
            if (selected.Count >= minimumNeighbours)
            {
                break;
            }
        }
    }

    private static List<DepthNeighbourCandidate> Candidates(ProjectData project, ReconstructionScene scene,
        CameraPose reference, Dictionary<string, Photo> photos, DepthCoverageSample[] samples, CancellationToken cancellation)
    {
        var candidates = new List<DepthNeighbourCandidate>();
        var centre = new V3(reference.Centre);
        foreach (var camera in scene.Cameras)
        {
            cancellation.ThrowIfCancellationRequested();
            if (camera.ImageId == reference.ImageId || !photos.TryGetValue(camera.ImageId, out var photo))
            {
                continue;
            }

            var distance = (new V3(camera.Centre) - centre).Length;
            if (!double.IsFinite(distance) || distance <= .05)
            {
                continue;
            }

            var quality = Coverage(photo, camera, centre, samples, project.Settings.DepthWidth);
            candidates.Add(new DepthNeighbourCandidate(camera, distance, quality));
        }

        return candidates;
    }

    private static byte[] Coverage(Photo photo, CameraPose camera, V3 referenceCentre, DepthCoverageSample[] samples, int depthWidth)
    {
        var quality = new byte[samples.Length];
        var projector = new CameraProjector(photo, camera);
        var centre = new V3(camera.Centre);
        var width = Math.Min(photo.Width, depthWidth);
        var margin = (width >= 1280 ? 5.0 : 3.0) * photo.Width / width;
        for (var index = 0; index < samples.Length; index++)
        {
            var point = samples[index].Position;
            var pixel = projector.Project(point);
            if (!pixel.HasValue)
            {
                continue;
            }

            var p = pixel.Value;
            var inside = p.X >= margin && p.X < photo.Width - margin
                && p.Y >= margin && p.Y < photo.Height - margin;
            if (!inside)
            {
                continue;
            }

            var cosine = (point - referenceCentre).Unit.Dot((point - centre).Unit);
            // Enough triangulation parallax, without preferring extreme viewpoints
            // whose common surface is likely foreshortened or occluded.
            var usefulParallax = cosine < .9999619230641713 && cosine >= .7071067811865476;
            if (!usefulParallax)
            {
                continue;
            }

            var sine = Math.Sqrt(Math.Max(0, 1 - cosine * cosine));
            quality[index] = (byte)Math.Clamp(255 * Math.Min(1, sine / .08715574274765817) * cosine, 1, 255);
        }

        return quality;
    }

    private static bool Redundant(DepthNeighbourCandidate candidate, List<DepthNeighbourCandidate> selected, double medianDepth)
    {
        var separation = Math.Max(.05, medianDepth * .001);
        foreach (var other in selected)
        {
            if (candidate.Camera.ImageId == other.Camera.ImageId || (candidate.Centre - other.Centre).Length < separation)
            {
                return true;
            }
        }

        return false;
    }

    private static double Score(DepthNeighbourCandidate candidate, DepthCoverageSample[] samples, int[] support, bool fillMinimum)
    {
        var gain = 0.0;
        var quality = 0.0;
        for (var index = 0; index < samples.Length; index++)
        {
            var value = candidate.Quality[index] / 255.0;
            quality += value * samples[index].Weight;
            if (value > 0 && support[index] < samples[index].RequiredViews)
            {
                gain += samples[index].Weight * (.5 + .5 * value);
            }
        }

        var redundantCoverage = gain == 0 && !fillMinimum;
        if (quality == 0 || redundantCoverage)
        {
            return 0;
        }
        // Cover missing first/second observations before spending slots on
        // redundant central coverage. Distance only breaks otherwise equal scores.
        return gain * 1000 + quality + 1 / (1 + candidate.Distance);
    }

    private static void Add(List<DepthNeighbourCandidate> selected, int[] support, DepthNeighbourCandidate candidate, double medianDepth)
    {
        var separation = Math.Max(.05, medianDepth * .001);
        var coincident = selected.Where(other => (candidate.Centre - other.Centre).Length < separation).ToArray();
        for (var index = 0; index < support.Length; index++)
        {
            if (candidate.Quality[index] > 0 && !coincident.Any(other => other.Quality[index] > 0))
            {
                support[index]++;
            }
        }

        selected.Add(candidate);
    }
}
