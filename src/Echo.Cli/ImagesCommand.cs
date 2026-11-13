using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Metadata;
using Echo.Projects;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ImagesCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IMetadataReader metadataReader;
    private readonly IMetadataPayloadReader payloadReader;

    [ImportingConstructor]
    public ImagesCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IMetadataReader metadataReader, IMetadataPayloadReader payloadReader)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.metadataReader = metadataReader;
        this.payloadReader = payloadReader;
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
            case "list":
                var set = args.Get("set", null);
                var query = args.Get("query", null);
                var images = store.Load().Images
                    .Where(image => set == null || image.Sets.Contains(set))
                    .Where(image => MatchesQuery(image, query))
                    .ToList();
                output.Write(images);
                return 0;
            case "enable":
                WriteReceipt(store.Edit(p => FindImage(p, args.RequiredWord(2)).Enabled = bool.Parse(args.Get("value", "true")!)));
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
