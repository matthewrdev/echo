using System.Diagnostics;
using System.Text.Json;
using Echo.Models;

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
        using var process = Process.Start(start) ?? throw new EchoException("Could not start the native renderer.", 5);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            JsonSerializer.Serialize(process.StandardInput.BaseStream, request, RenderJsonContext.Default.ModelRenderRequest);
            process.StandardInput.Close();
            process.WaitForExitAsync(cancellation).GetAwaiter().GetResult();
            cancellation.ThrowIfCancellationRequested();
            var diagnostic = stderr.GetAwaiter().GetResult().Trim();
            if (process.ExitCode != 0)
            {
                throw new EchoException("Native render failed: " + diagnostic, 5);
            }

            var result = JsonSerializer.Deserialize(stdout.GetAwaiter().GetResult(), RenderJsonContext.Default.ModelRenderResult)
                ?? throw new EchoException("The native renderer returned no result.", 5);
            var invalidResult = result.Input != request.Input || result.Output != request.Output
                || result.Width != request.Width || result.Height != request.Height
                || result.View != request.View || result.Mode != request.Mode
                || result.CameraImageId != request.Camera?.ImageId
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
                : throw new EchoException("ECHO_RENDERER does not name an existing EchoRender executable.", 5);
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "EchoRender");
        return File.Exists(bundled) ? bundled
            : throw new EchoException("Native renderer not found. Run scripts/echo-photo, use Echo.app's bundled CLI, or set ECHO_RENDERER to EchoRender.", 5);
    }
}
