using System.Diagnostics;
using Echo.Cli.Commands.Export;
using Echo.Cli;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Rendering;

namespace Echo.Tests;

public sealed class RenderCommandTests
{
    private readonly IArgumentParser parser;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly ICommandOutput output;
    private readonly IModelRenderer native;

    public RenderCommandTests(IArgumentParser parser, IProjectStoreFactory stores, IFileOperations files,
        ICommandOutput output, IModelRenderer native)
    {
        this.parser = parser;
        this.stores = stores;
        this.files = files;
        this.output = output;
        this.native = native;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Render selects the requested export without modifying project inputs", () => WithProject(store =>
        {
            var run = Guid.NewGuid().ToString("N");
            var model = store.PathFor("artifacts/export/saved/cliff.glb");
            Directory.CreateDirectory(Path.GetDirectoryName(model)!);
            File.WriteAllText(model, "saved model");
            store.SaveRun(new RunData
            {
                Id = run,
                Status = "completed",
                Artifacts = new Dictionary<string, string> { ["export"] = "artifacts/export/saved" }
            });
            var projectBefore = File.ReadAllBytes(store.PathFor("project.bin"));
            var recording = new RecordingModelRenderer();
            var command = new RenderCommand(stores, recording, output);
            var destination = store.PathFor("previews/rendered model.png");
            var arguments = parser.Parse(["--project", store.Root, "render", "--run", run, "--output", destination,
                "--view", "oblique", "--mode", "normals", "--width", "1920", "--height", "1080"]);
            Check(command.Execute(arguments) == 0, "Render command succeeds");
            Check(recording.Request == new ModelRenderRequest(model, destination, 1920, 1080, "oblique", "normals"), "Selected model and camera forwarded");
            Check(File.ReadAllBytes(store.PathFor("project.bin")).SequenceEqual(projectBefore), "Project unchanged");
            Check(File.ReadAllText(model) == "saved model", "Model unchanged");
        }));

        tests.Run("Render rejects ambiguous sources, missing exports, invalid dimensions and existing outputs", () => WithProject(store =>
        {
            var model = store.PathFor("model.glb");
            var destination = store.PathFor("preview.png");
            File.WriteAllText(model, "model");
            var recording = new RecordingModelRenderer();
            var command = new RenderCommand(stores, recording, output);
            Reject(command, ["render", model, "--project", store.Root, "--output", destination]);
            Reject(command, ["render", model, "--run", "latest", "--output", destination]);
            Reject(command, ["render", model, "--output", destination, "--width", "8192", "--height", "8192"]);
            Reject(command, ["render", model, "--output", destination, "--width", "99999999999999999"]);
            Reject(command, ["render", model, "--output", destination, "--height", "0"]);
            Reject(command, ["render", model, "--output", destination, "--view", "unknown"]);
            Reject(command, ["render", model, "--output", destination, "--mode", "unknown"]);
            Reject(command, ["render", model, "--output", destination, "--yaw", "45"]);
            Reject(command, ["render", model, "--output", store.PathFor("preview.jpg")]);
            var run = Guid.NewGuid().ToString("N");
            store.SaveRun(new RunData { Id = run, Status = "completed" });
            Reject(command, ["--project", store.Root, "render", "--output", destination]);
            File.WriteAllText(destination, "keep this image");
            Reject(command, ["render", model, "--output", destination]);
            Check(File.ReadAllText(destination) == "keep this image", "Existing output preserved");
            Check(recording.Request == null, "Invalid requests never start rendering");
        }));

        tests.Run("Native renderer cancels its child and removes unpublished output", () =>
        {
            WithProject(store =>
            {
                if (!OperatingSystem.IsMacOS())
                {
                    return;
                }

                var helper = store.PathFor("render helper");
                File.WriteAllText(helper, "#!/bin/sh\ncat > /dev/null\nsleep 30\n");
                File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var previous = Environment.GetEnvironmentVariable("ECHO_RENDERER");
                Environment.SetEnvironmentVariable("ECHO_RENDERER", helper);
                try
                {
                    var model = store.PathFor("model.glb");
                    File.WriteAllText(model, "model");
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                    var timer = Stopwatch.StartNew();
                    try
                    {
                        native.Render(new ModelRenderRequest(model, store.PathFor("preview.png")), cancellation.Token);
                        throw new InvalidOperationException("Expected cancellation");
                    }
                    catch (OperationCanceledException)
                    {
                        Check(timer.Elapsed < TimeSpan.FromSeconds(5), "Child stopped promptly");
                        Check(!File.Exists(store.PathFor("preview.png")), "No published output");
                        Check(!Directory.EnumerateFiles(store.Root, ".echo-render-*").Any(), "No temporary output");
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("ECHO_RENDERER", previous);
                }
            });
        });
    }

    private void Reject(RenderCommand command, string[] input)
    {
        try
        {
            command.Execute(parser.Parse(input));
            throw new InvalidOperationException("Expected invalid render request");
        }
        catch (EchoException error)
        {
            Check(error.ExitCode == 2, "Invalid input reported as usage error");
        }
    }

    private void WithProject(Action<IProjectStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-render-test-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init("Render test");
        try
        {
            action(store);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
