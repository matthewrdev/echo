using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Echo.Models;

namespace Echo.Rendering;

public sealed class NativeToolProcess : INativeToolProcess
{
    public NativeToolProcess()
    {
    }

    public TResult Invoke<TRequest, TResult>(string? operation, TRequest request, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResult> resultType, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            throw new EchoException("This operation requires the macOS Echo native helper.", 5);
        }

        var start = new ProcessStartInfo(FindExecutable())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (operation != null)
        {
            start.ArgumentList.Add(operation);
        }

        using var process = Start(start);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            JsonSerializer.Serialize(process.StandardInput.BaseStream, request, requestType);
            process.StandardInput.Close();
            process.WaitForExitAsync(cancellation).GetAwaiter().GetResult();
            cancellation.ThrowIfCancellationRequested();
            var diagnostic = stderr.GetAwaiter().GetResult().Trim();
            if (process.ExitCode != 0)
            {
                throw new EchoException("Native operation failed: " + diagnostic, 5);
            }

            return JsonSerializer.Deserialize(stdout.GetAwaiter().GetResult(), resultType)
                ?? throw new EchoException("The Echo native helper returned no result.", 5);
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

    private Process Start(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new EchoException("Could not start the Echo native helper.", 5);
        }
        catch (Win32Exception error)
        {
            throw new EchoException("Could not start the Echo native helper: " + error.Message, 5);
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
            : throw new EchoException("Native renderer not found. Run scripts/echo, use Echo.app's bundled CLI, or set ECHO_RENDERER to EchoRender.", 5);
    }
}
