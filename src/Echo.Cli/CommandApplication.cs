using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;

namespace Echo.Cli;

[Export(typeof(ICommandApplication)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CommandApplication : ICommandApplication
{
    private readonly IArgumentParser argumentParser;
    private readonly ICommandOutput output;
    private readonly IEnumerable<ICliCommand> commands;

    private const string Help = """
    Echo 0.1 — experimental C# photogrammetry workbench

    echo-photo project init PATH [--name NAME]
    echo-photo --project PATH project info
    echo-photo --project PATH project rename --name NAME
    echo-photo --project PATH project delete --confirm PROJECT_ID
    echo-photo --project PATH images import DIRECTORY [--set NAME] [--limit N]
    echo-photo --project PATH images deduplicate [--apply]
    echo-photo --project PATH images restore IMAGE_ID
    echo-photo --project PATH images list [--set NAME] [--query TEXT]
    echo-photo --project PATH images metadata IMAGE_ID
    echo-photo --project PATH images payload IMAGE_ID GROUP:TAG
    echo-photo --project PATH images enable IMAGE_ID --value true|false
    echo-photo --project PATH sets list
    echo-photo --project PATH sets assign IMAGE_ID --set NAME
    echo-photo --project PATH pipeline reset --from STAGE
    echo-photo --project PATH storage list
    echo-photo --project PATH storage plan [--artifact artifacts/STAGE/KEY]
    echo-photo --project PATH storage delete --confirm TOKEN [--artifact artifacts/STAGE/KEY]
    echo-photo --project PATH storage plan --unused [--keep-runs RUN_ID,RUN_ID]
    echo-photo --project PATH storage delete --unused [--keep-runs RUN_ID,RUN_ID] --confirm TOKEN
    echo-photo --project PATH config show
    echo-photo --project PATH config import FILE
    echo-photo --project PATH hints list|import FILE
    echo-photo --project PATH hints camera IMAGE_ID [--lat N --lon N --alt N --alt-datum TEXT] [--yaw N --pitch N --roll N]
    echo-photo --project PATH hints overlap IMAGE_ID OTHER_ID
    echo-photo --project PATH hints tie IMAGE_ID OTHER_ID --ax X --ay Y --bx X --by Y
    echo-photo --project PATH hints disable HINT_ID
    echo-photo --project PATH pipeline plan [--target export]
    echo-photo --project PATH run start [--target alignment] [--events ndjson]
        [--colour-tuning on|off|0..1] [--edge-softening on|off|0..16]
        [--subject-distance auto|off|METRES]
    echo-photo --project PATH run resume RUN_ID [--events ndjson]
    echo-photo --project PATH run status|cancel|events [RUN_ID]
    echo-photo --project PATH trace list [--all] [--run RUN_ID]
    echo-photo --project PATH trace show ATTEMPT_ID [--run RUN_ID] [--summary]
    echo-photo --project PATH trace export ATTEMPT_ID --output NEW_FILE [--format html|json] [--run RUN_ID]
    echo-photo --project PATH inspect [--run RUN_ID] [--summary]
    echo-photo --project PATH export-gis --output NEW_DIRECTORY [--run RUN_ID]
    echo-photo inspect-glb FILE
    echo-photo compare-glb FILE REFERENCE
    echo-photo render FILE.glb --output NEW_FILE.png
    echo-photo --project PATH render --output NEW_FILE.png [--run RUN_ID]
        [--width 1600 --height 1000] [--view front|back|left|right|top|oblique]
        [--mode textured|solid|wireframe|normals]
    echo-photo doctor
    echo-photo runtime-info

    All data responses are JSON. Long jobs emit NDJSON with --events ndjson;
    otherwise progress goes to stderr and the final result is JSON.
    Stages: features → matching → alignment → depth → cloud → fusion → mesh → texture → export.
    Texture defaults: colour tuning 0.5; edge softening 4 atlas pixels.
    Run options override only the new run; saved project settings are unchanged.
    Cancellation: Ctrl-C or run cancel RUN_ID. Resume preserves the original inputs.
    Outputs live in immutable project artifacts; input photos are never modified.
    Rendering requires macOS/Metal and an exported Echo GLB; the project default is its latest run.
    Render writes a new PNG without opening a window. Maximum size: 8192 per side, 16,777,216 pixels total.
    """;

    [ImportingConstructor]
    public CommandApplication(IArgumentParser argumentParser, ICommandOutput output, [ImportMany] IEnumerable<ICliCommand> commands)
    {
        this.argumentParser = argumentParser;
        this.output = output;
        this.commands = commands;
    }

    public int Run(string[] input)
    {
        try
        {
            var arguments = argumentParser.Parse(input);
            if (arguments.Words.Count == 0 || arguments.Options.ContainsKey("help"))
            {
                Console.WriteLine(Help);
                return 0;
            }

            var commandName = arguments.Words[0];
            var command = commands.SingleOrDefault(candidate => candidate.Names.Contains(commandName));
            if (command == null)
            {
                throw new EchoException("Unknown command. Use --help.", 2);
            }

            return command.Execute(arguments);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Processing stopped. Saved results are still available. Resume reuses saved work and repeats unfinished steps.");
            return 130;
        }
        catch (Exception error)
        {
            var code = error switch
            {
                EchoException echo => echo.ExitCode,
                FormatException or JsonException or ArgumentException => 2,
                _ => 4
            };
            output.Error(error.Message, code);
            return code;
        }
    }
}
