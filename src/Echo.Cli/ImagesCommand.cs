using Echo.Contracts;
using Echo.Metadata;
using Echo.Projects;

namespace Echo.Cli;

public sealed class ImagesCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IMetadataReader metadataReader;
    private readonly IMetadataPayloadReader payloadReader;
    private readonly IPhotoDeduplicator deduplicator;
    private readonly IPhotoDeletionService deletion;

    public ImagesCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IMetadataReader metadataReader,
        IMetadataPayloadReader payloadReader, IPhotoDeduplicator deduplicator, IPhotoDeletionService deletion)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.metadataReader = metadataReader;
        this.payloadReader = payloadReader;
        this.deduplicator = deduplicator;
        this.deletion = deletion;
    }

    public IReadOnlyCollection<string> Names => ["images"];

    public int Execute(Arguments args)
    {
        var action = args.Word(1, "");
        var ndjson = args.IsNdjson;
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        store.Load();
        switch (action)
        {
            case "import":
                output.Write(new
                {
                    imported = store.Import(args.RequiredWord(2), args.Get("set", "default")!, args.Int("limit", int.MaxValue), value => output.Progress(value, ndjson))
                });
                return 0;
            case "deduplicate":
                var plan = deduplicator.Plan(store);
                var apply = args.Options.ContainsKey("apply");
                if (apply)
                {
                    deduplicator.Apply(store, plan);
                }
                output.Write(new
                {
                    applied = apply,
                    plan
                });
                return 0;
            case "derive-iphone-poses":
                DeriveIPhonePoses(store);
                return 0;
            case "list":
                var set = args.Get("set", null);
                var query = args.Get("query", null);
                var images = store.Load().Images
                    .Where(image => set == null || image.Sets.Contains(set))
                    .Where(image => MatchesQuery(image, query))
                    .ToList();
                output.Write(images);
                return 0;
            case "restore":
                deduplicator.Restore(store, args.RequiredWord(2));
                WriteReceipt(store.Load());
                return 0;
            case "delete":
                if (args.Get("confirm", null) != store.Load().Id)
                {
                    throw new EchoException("Photo deletion requires --confirm with the current project ID.", 2);
                }

                WriteReceipt(deletion.Delete(store, args.Words.Skip(2).ToArray()));
                return 0;
            case "enable":
                var imageId = args.RequiredWord(2);
                var enabled = bool.Parse(args.Get("value", "true")!);
                if (args.Words.Count > 3)
                {
                    SetMultipleEnabled(store, args.Words.Skip(2), enabled);
                }
                else if (enabled && FindImage(store.Load(), imageId).Discarded)
                {
                    deduplicator.Restore(store, imageId);
                    WriteReceipt(store.Load());
                }
                else
                {
                    WriteReceipt(store.Edit(p => FindImage(p, imageId).Enabled = enabled));
                }
                return 0;
            case "metadata":
            case "payload":
                var photo = FindImage(store.Load(), args.RequiredWord(2));
                var original = Directory.GetFiles(store.PathFor("sources"), photo.Id + ".original.*").SingleOrDefault();
                if (original == null)
                {
                    throw new EchoException("The original photograph is missing; its complete metadata cannot be read.");
                }

                if (action == "payload")
                {
                    output.Write(payloadReader.Read(original, args.RequiredWord(3)));
                }
                else
                {
                    output.Write(metadataReader.Read(original, includeAllGroups: true));
                }
                return 0;
            default:
                throw new EchoException("Unknown images action. Use --help.", 2);
        }
    }

    private void SetMultipleEnabled(IProjectStore store, IEnumerable<string> ids, bool enabled)
    {
        WriteReceipt(store.Edit(project =>
        {
            var photos = ids.Distinct(StringComparer.Ordinal).Select(id => FindImage(project, id)).ToArray();
            foreach (var photo in photos)
            {
                photo.Enabled = enabled;
                if (enabled && photo.Discarded)
                {
                    photo.Discarded = false;
                    photo.DuplicateOf = null;
                    photo.KeepWhenTrimming = true;
                }
            }
        }));
    }

    private void DeriveIPhonePoses(IProjectStore store)
    {
        using var import = store.Lock("import");
        var estimates = new Dictionary<string, Attitude>(StringComparer.Ordinal);
        foreach (var photo in store.Load().Images)
        {
            if (photo.Attitude == null && IPhoneCapturePose.Estimate(photo.Metadata) is { } estimate)
            {
                estimates.Add(photo.Id, estimate);
            }
        }

        var updated = 0;
        if (estimates.Count > 0)
        {
            store.Edit(project =>
            {
                foreach (var photo in project.Images)
                {
                    if (photo.Attitude == null && estimates.TryGetValue(photo.Id, out var estimate))
                    {
                        photo.Attitude = estimate;
                        updated++;
                    }
                }
            });
        }

        output.Write(new
        {
            updated
        });
    }

    private Photo FindImage(ProjectData project, string id)
    {
        return project.Images.Find(image => image.Id == id) ?? throw new EchoException("Unknown image ID.", 2);
    }

    private bool MatchesQuery(Photo photo, string? query)
    {
        var matchesQuery = query == null
            || photo.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || photo.Camera.Contains(query, StringComparison.OrdinalIgnoreCase);
        return matchesQuery;
    }

    private void WriteReceipt(ProjectData project)
    {
        output.Write(new
        {
            project.Id,
            project.Revision
        });
    }
}
