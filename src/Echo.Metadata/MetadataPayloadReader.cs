using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Echo.Contracts;

namespace Echo.Metadata;

[Export(typeof(IMetadataPayloadReader)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MetadataPayloadReader : IMetadataPayloadReader
{
    private readonly IMetadataReader metadata;

    [ImportingConstructor]
    public MetadataPayloadReader(IMetadataReader metadata)
    {
        this.metadata = metadata;
    }

    public byte[] Read(string path, string tag)
    {
        if (!Regex.IsMatch(tag, "^[A-Za-z0-9][A-Za-z0-9:_-]{0,127}$"))
        {
            throw new EchoException("Invalid metadata tag name.", 2);
        }
        var start = new ProcessStartInfo(metadata.FindExifTool() ?? throw new EchoException("ExifTool is required to read original metadata."))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "-b", "-u", "-" + tag, "--", Path.GetFullPath(path) })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            using var payload = new MemoryStream();
            var buffer = new byte[65536];
            while (true)
            {
                var count = process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult();
                if (count == 0)
                {
                    break;
                }
                if (payload.Length + count > 16 * 1024 * 1024)
                {
                    throw new EchoException("This field exceeds the 16 MB metadata viewer limit.");
                }
                payload.Write(buffer.AsSpan(0, count));
            }
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new EchoException("ExifTool failed: " + error.GetAwaiter().GetResult());
            }
            if (payload.Length == 0)
            {
                throw new EchoException("No payload was returned for " + tag + ".");
            }
            return payload.ToArray();
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
}
