using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Benchmarks;

[Export]
public sealed class PairVerificationBenchmark
{
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;

    [ImportingConstructor]
    public PairVerificationBenchmark(IProjectStoreFactory stores, IJsonCodec json, IFileOperations files)
    {
        this.stores = stores;
        this.json = json;
        this.files = files;
    }

    public int Run(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks verify-pair PROJECT RUN LEFT_NAME RIGHT_NAME OUTPUT.json");
            return 2;
        }
        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        var left = run.Snapshot.Images.Single(photo => photo.Name == args[2]);
        var right = run.Snapshot.Images.Single(photo => photo.Name == args[3]);
        foreach (var unit in json.Read<List<string>>(store.PathFor(run.Artifacts["matching"] + "/units.json")))
        {
            var pair = json.Read<ImagePair>(store.PathFor(unit + "/pair.json"));
            var forward = pair.A == left.Id && pair.B == right.Id;
            var backward = pair.B == left.Id && pair.A == right.Id;
            if (!forward && !backward)
            {
                continue;
            }
            var verified = FundamentalMatrixEstimator.Estimate(pair.Matches, Math.Max(left.Width, right.Width) / 1000.0, default);
            var decisions = new List<object>();
            for (var index = 0; index < pair.Matches.Count; index++)
            {
                var match = pair.Matches[index];
                decisions.Add(new
                {
                    number = index + 1,
                    match,
                    accepted = verified?.Inliers.Contains(match) == true
                });
            }
            files.AtomicJson(Path.GetFullPath(args[4]), new
            {
                unit,
                pair.A,
                pair.B,
                candidates = pair.Matches.Count,
                inliers = verified?.Inliers.Count ?? 0,
                lines = pair.LineMatches.Count,
                decisions
            });
            Console.WriteLine($"{pair.Matches.Count} point candidates; {verified?.Inliers.Count ?? 0} geometrically consistent; {pair.LineMatches.Count} unverified lines.");
            return 0;
        }
        throw new EchoException("The selected run does not contain this image pair.");
    }
}
