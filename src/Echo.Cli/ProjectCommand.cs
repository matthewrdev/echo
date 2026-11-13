using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Projects;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ProjectCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;

    [ImportingConstructor]
    public ProjectCommand(IProjectStoreFactory storeFactory, ICommandOutput output)
    {
        this.storeFactory = storeFactory;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["project"];

    public int Execute(Arguments arguments)
    {
        if (arguments.Word(1, "") != "init")
        {
            throw new EchoException("Expected project init PATH.", 2);
        }

        var project = storeFactory.Open(arguments.RequiredWord(2));
        project.Init(arguments.Get("name", null));
        output.Write(new
        {
            project = project.Root,
            data = project.Load()
        });
        return 0;
    }
}
