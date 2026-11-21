using System.Globalization;
using Echo.Contracts;
using Echo.Projects;
using Echo.Rendering;

namespace Echo.Cli;

public sealed class RenderCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IModelRenderer renderer;
    private readonly ICommandOutput output;

    public RenderCommand(IProjectStoreFactory stores, IModelRenderer renderer, ICommandOutput output)
    {
        this.stores = stores;
        this.renderer = renderer;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["render"];

    public int Execute(Arguments arguments)
    {
        var supported = new HashSet<string> { "project", "run", "output", "width", "height", "view", "mode", "json" };
        if (arguments.Options.Keys.Any(key => !supported.Contains(key)))
        {
            throw new EchoException("Unsupported render option. Use --help for render options.", 2);
        }

        var destination = arguments.Get("output", null) ?? throw new EchoException("--output FILE.png is required.", 2);
        var request = new ModelRenderRequest(ResolveInput(arguments), Path.GetFullPath(destination),
            Dimension(arguments, "width", 1600), Dimension(arguments, "height", 1000),
            arguments.Get("view", "front")!, arguments.Get("mode", "textured")!);
        request.Validate();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            output.Write(renderer.Render(request, cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            output.Error("Rendering cancelled. No image was published.", 130);
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }

        return 0;
    }

    private string ResolveInput(Arguments arguments)
    {
        var project = arguments.Get("project", null);
        if (project == null)
        {
            if (arguments.Words.Count != 2 || arguments.Options.ContainsKey("run"))
            {
                throw new EchoException("Use render FILE.glb or --project PATH render [--run RUN_ID].", 2);
            }

            return Path.GetFullPath(arguments.Words[1]);
        }

        if (arguments.Words.Count != 1)
        {
            throw new EchoException("Choose either --project PATH or a GLB file, not both.", 2);
        }

        var store = stores.Open(project);
        store.Load();
        var run = store.GetRun(arguments.Get("run", "latest")!);
        if (!run.Artifacts.TryGetValue("export", out var export))
        {
            throw new EchoException("The selected run has no exported model. Select an exported run with --run or process through export first.", 2);
        }

        var path = Path.GetFullPath(Path.Combine(store.PathFor(export), "cliff.glb"));
        var artifactRoot = Path.GetFullPath(store.PathFor("artifacts")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(artifactRoot, StringComparison.Ordinal))
        {
            throw new EchoException("The run's export path is outside project artifacts.", 2);
        }

        return path;
    }

    private int Dimension(Arguments arguments, string key, int fallback)
    {
        var text = arguments.Get(key, null);
        if (text == null)
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new EchoException($"--{key} must be an integer from 1 to 8192.", 2);
        }

        return value;
    }
}
