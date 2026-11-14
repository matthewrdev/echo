using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text.Json;
using Echo.Contracts;

namespace Echo.Metadata;

[Export(typeof(IMetadataReader)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MetadataReader : IMetadataReader
{
    [ImportingConstructor]
    public MetadataReader()
    {
    }

    public string? FindExifTool()
    {
        var configured = Environment.GetEnvironmentVariable("ECHO_EXIFTOOL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        var name = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
        var bundled = Path.Combine(AppContext.BaseDirectory, "metadata", name);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        // Source builds may opt into an external tool; packaged apps carry their own.
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public Dictionary<string, JsonElement> Read(string path, bool includeAllGroups = false)
    {
        var start = new ProcessStartInfo(FindExifTool() ?? throw new EchoException("The metadata reader is missing. Rebuild the Echo app package, or set ECHO_EXIFTOOL for a source build."))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (includeAllGroups)
        {
            start.ArgumentList.Add("-a");
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add("-G1");
        }

        foreach (var arg in new[]
        {
            "-j",
            "-n",
            "--",
            Path.GetFullPath(path)
        }
        )
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new EchoException("ExifTool failed: " + error.GetAwaiter().GetResult());
            }

            return JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(output)![0];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new EchoException("The metadata reader could not start. Rebuild the Echo app package, or check ECHO_EXIFTOOL for a source build.");
        }
    }
}
