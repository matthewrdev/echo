using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Echo.Contracts;
using Echo.Infrastructure;

namespace Echo.Telemetry;

public sealed class PerformanceTraceExporter : IPerformanceTraceExporter
{
    private readonly IPerformanceTraceStore traces;
    private readonly IJsonCodec json;

    public PerformanceTraceExporter(IPerformanceTraceStore traces, IJsonCodec json)
    {
        this.traces = traces;
        this.json = json;
    }

    public PerformanceTraceExport Export(string runDirectory, string attemptId, string destination, string format)
    {
        if (format is not ("json" or "html"))
        {
            throw new EchoException("Trace export format must be json or html.", 2);
        }
        var path = System.IO.Path.GetFullPath(destination);
        if (System.IO.Path.Exists(path))
        {
            throw new EchoException("Trace export output already exists. Choose a new file.", 2);
        }
        var document = traces.Read(runDirectory, attemptId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (format == "json")
                {
                    json.Write(stream, document);
                }
                else
                {
                    WriteHtml(stream, document);
                }
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: false);
            return new PerformanceTraceExport(path, format, document.Summary.RunId, document.Summary.Id,
                document.Spans.Count, document.Summary.DroppedSpans, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private void WriteHtml(Stream output, PerformanceTraceDocument document)
    {
        using var resource = typeof(PerformanceTraceExporter).Assembly.GetManifestResourceStream("Echo.Telemetry.PerformanceTraceViewer.html")
            ?? throw new InvalidOperationException("The embedded trace viewer is missing.");
        using var reader = new StreamReader(resource);
        var template = reader.ReadToEnd();
        const string marker = "__ECHO_TRACE_DATA__";
        var location = template.IndexOf(marker, StringComparison.Ordinal);
        if (location < 0)
        {
            throw new InvalidOperationException("The trace viewer has no data placeholder.");
        }
        output.Write(Encoding.UTF8.GetBytes(template[..location]));
        // Keep script end tags, HTML and Unicode line separators inside JSON data.
        // The viewer inserts all user-provided labels with textContent, never HTML.
        var options = new JsonSerializerOptions(json.Options) { WriteIndented = false, Encoder = JavaScriptEncoder.Default };
        JsonSerializer.Serialize(output, document, new CoreJsonReadContext(options).PerformanceTraceDocument);
        output.Write(Encoding.UTF8.GetBytes(template[(location + marker.Length)..]));
    }
}
