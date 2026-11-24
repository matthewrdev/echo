using Echo.Infrastructure;
using Echo.Models;
using Echo.Metadata;
using Echo.Projects;

namespace Echo.Cli.Commands.Images;

public sealed class ImagesCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IMetadataReader metadataReader;
    private readonly IMetadataPayloadReader payloadReader;
    private readonly IPhotoDeduplicator deduplicator;
    private readonly IPhotoDeletionService deletion;
    private readonly IJsonCodec jsonCodec;

    public ImagesCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IMetadataReader metadataReader,
        IMetadataPayloadReader payloadReader, IPhotoDeduplicator deduplicator, IPhotoDeletionService deletion,
        IJsonCodec jsonCodec)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.metadataReader = metadataReader;
        this.payloadReader = payloadReader;
        this.deduplicator = deduplicator;
        this.deletion = deletion;
        this.jsonCodec = jsonCodec;
    }

    public IReadOnlyCollection<string> Names => ["images"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("images import",
            "--project PATH images import DIRECTORY [--set NAME] [--limit N]",
            "Import supported photos and retain their original metadata."),
        new("images import",
            "--project PATH images import --manifest FILE",
            "Import an explicit JSON list of photo paths and set names."),
        new("images derive-iphone-poses",
            "--project PATH images derive-iphone-poses",
            "Derive camera pose hints from supported iPhone photos."),
        new("images deduplicate",
            "--project PATH images deduplicate [--apply]",
            "Find duplicate photos. Pass --apply to mark duplicates."),
        new("images list",
            "--project PATH images list [--set NAME] [--query TEXT]",
            "List photos with optional set and text filters."),
        new("images restore",
            "--project PATH images restore IMAGE_ID",
            "Restore a discarded photo."),
        new("images delete",
            "--project PATH images delete IMAGE_ID [IMAGE_ID ...] --confirm PROJECT_ID",
            "Delete photos after confirming the project ID."),
        new("images enable",
            "--project PATH images enable IMAGE_ID [IMAGE_ID ...] --value true|false",
            "Select which photos can be used for processing."),
        new("images metadata",
            "--project PATH images metadata IMAGE_ID",
            "Read a photo’s original metadata tags."),
        new("images payload",
            "--project PATH images payload IMAGE_ID GROUP:TAG",
            "Read the original binary value of a metadata tag."),
    ];

    public int Execute(Arguments args)
    {
        var action = args.Word(1, "");
        var ndjson = args.IsNdjson;
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        store.Load();
        switch (action)
        {
            case "import":
                Import(store, args, ndjson);
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

    private void Import(IProjectStore store, Arguments args, bool ndjson)
    {
        void Report(object value) => output.Progress(value, ndjson);

        var manifest = args.Get("manifest", null);
        var hasDirectoryOptions = args.Words.Count != 2 || args.Options.ContainsKey("set") || args.Options.ContainsKey("limit");
        if (manifest != null && hasDirectoryOptions)
        {
            throw new EchoException("Use --manifest without a directory, --set or --limit.", 2);
        }

        var imported = manifest == null
            ? store.Import(args.RequiredWord(2), args.Get("set", "default")!, args.Int("limit", int.MaxValue), Report)
            : store.Import(jsonCodec.Read<PhotoImportEntry[]>(manifest), Report);
        output.Write(new
        {
            imported
        });
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
