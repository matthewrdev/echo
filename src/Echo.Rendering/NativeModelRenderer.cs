using System.Diagnostics;
using System.Text.Json;
using Echo.Contracts;

namespace Echo.Rendering;

public sealed class NativeModelRenderer : IModelRenderer
{
    public NativeModelRenderer()
    {
    }

    public ModelRenderResult Render(ModelRenderRequest request, CancellationToken cancellation = default)
    {
        request = request with
        {
            Input = Path.GetFullPath(request.Input),
            Output = Path.GetFullPath(request.Output)
        };
        request.Validate();
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            throw new EchoException("Rendering requires macOS and a Metal-capable GPU.", 5);
        }

        var executable = FindExecutable();
        var directory = Path.GetDirectoryName(request.Output)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".echo-render-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var result = RunNative(executable, request with
            {
                Output = temporary
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, request.Output, overwrite: false);
            return result with
            {
                Output = request.Output
            };
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private ModelRenderResult RunNative(string executable, ModelRenderRequest request, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--render");
        using var process = Process.Start(start) ?? throw new EchoException("Could not start the native renderer.", 5);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            process.StandardInput.Write(JsonSerializer.Serialize(request, json));
            process.StandardInput.Close();
            process.WaitForExitAsync(cancellation).GetAwaiter().GetResult();
            cancellation.ThrowIfCancellationRequested();
            var diagnostic = stderr.GetAwaiter().GetResult().Trim();
            if (process.ExitCode != 0)
            {
                throw new EchoException("Native render failed: " + diagnostic, 5);
            }

            var result = JsonSerializer.Deserialize<ModelRenderResult>(stdout.GetAwaiter().GetResult(), json)
                ?? throw new EchoException("The native renderer returned no result.", 5);
            var invalidResult = result.Input != request.Input || result.Output != request.Output
                || result.Width != request.Width || result.Height != request.Height
                || result.View != request.View || result.Mode != request.Mode
                || result.Vertices <= 0 || result.Triangles <= 0 || !File.Exists(request.Output);
            if (invalidResult)
            {
                throw new EchoException("The native renderer returned an incomplete result.", 5);
            }

            return result;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }

    private string FindExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("ECHO_RENDERER");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? Path.GetFullPath(configured)
                : throw new EchoException("ECHO_RENDERER does not name an existing EchoDesktop executable.", 5);
        }

        foreach (var relative in new[] { "EchoDesktop", "../../MacOS/EchoDesktop" })
        {
            var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relative));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "macos", "Package.swift")))
            {
                continue;
            }

            foreach (var configuration in new[] { "release", "debug" })
            {
                var candidate = Path.Combine(directory.FullName, "macos", ".build", configuration, "EchoDesktop");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new EchoException("Native renderer not found. Use Echo.app's bundled CLI, build with 'swift build --package-path macos -c release', or set ECHO_RENDERER to EchoDesktop.", 5);
    }
}
