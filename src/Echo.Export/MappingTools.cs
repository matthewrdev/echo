using System.ComponentModel;
using System.Diagnostics;
using Echo.Models;

namespace Echo.Export;

public sealed class MappingTools : IMappingTools
{
    public string Run(string executable, params string[] arguments)
    {
        var configured = Environment.GetEnvironmentVariable(executable == "pdal" ? "ECHO_PDAL" : "ECHO_GDAL_BIN");
        var path = string.IsNullOrWhiteSpace(configured)
            ? FindExecutable(executable)
            : executable == "pdal" ? configured : Path.Combine(configured, executable);
        var start = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["PROJ_NETWORK"] = "OFF";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new EchoException($"Could not start {executable}.", 2);
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var standardOutput = output.GetAwaiter().GetResult();
            var diagnostics = errors.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new EchoException($"{executable} failed: {diagnostics.Trim()}", 2);
            }

            return standardOutput;
        }
        catch (Win32Exception error)
        {
            throw new EchoException($"Could not start {executable}: {error.Message}. Install PDAL and GDAL or set ECHO_PDAL and ECHO_GDAL_BIN.", 2);
        }
    }

    private string FindExecutable(string name)
    {
        foreach (var directory in new[] { "/opt/homebrew/bin", "/usr/local/bin" })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return name;
    }
}
