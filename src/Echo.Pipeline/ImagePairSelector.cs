using System.ComponentModel.Composition;
using Echo.Contracts;

namespace Echo.Pipeline;

[Export(typeof(IImagePairSelector)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ImagePairSelector : IImagePairSelector
{

    [ImportingConstructor]
    public ImagePairSelector()
    {
    }

    public IReadOnlyList<PairIdentity> Select(ProjectData project, IReadOnlyList<Photo> images, IReadOnlyDictionary<string, string> featureUnits)
    {
        var wanted = new Dictionary<string, PairIdentity>();
        void AddPair(string a, string b)
        {
            if (string.CompareOrdinal(a, b) > 0)
            {
                var temp = a;
                a = b;
                b = temp;
            }

            var availablePair = a != b
                && featureUnits.ContainsKey(a)
                && featureUnits.ContainsKey(b);
            if (availablePair)
            {
                wanted.TryAdd(a + ":" + b, new PairIdentity
                {
                    A = a,
                    B = b
                });
            }
        }

        for (var i = 0; i < images.Count; i++)
        {
            var end = project.Settings.MatchAllPairs ? images.Count : Math.Min(images.Count, i + 1 + project.Settings.PairWindow);
            for (var j = i + 1; j < end; j++)
            {
                AddPair(images[i].Id, images[j].Id);
            }
        }

        foreach (var image in images.Where(i => i.Gps != null))
        {
            foreach (var other in images.Where(candidate => candidate.Gps != null && candidate.Id != image.Id)
                .OrderBy(candidate => Math.Pow(candidate.Gps!.Latitude - image.Gps!.Latitude, 2)
                    + Math.Pow(candidate.Gps.Longitude - image.Gps.Longitude, 2))
                .Take(project.Settings.GpsPairNeighbours))
            {
                AddPair(image.Id, other.Id);
            }
        }

        foreach (var hint in project.Hints.Where(h => h.Enabled && h.Type is "overlap" or "tie"))
        {
            if (hint.OtherImageId != null)
            {
                AddPair(hint.ImageId, hint.OtherImageId);
            }
        }

        return wanted.Values.ToArray();
    }
}
