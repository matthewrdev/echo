using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;
using MetadataExtractor;
using MetadataExtractor.Formats.Jpeg;

namespace Echo.Metadata;

[Export(typeof(IMetadataReader)), Export(typeof(IMetadataPayloadReader)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MetadataReader : IMetadataReader, IMetadataPayloadReader
{
    [ImportingConstructor]
    public MetadataReader()
    {
    }

    public string Description => "MetadataExtractor " + typeof(ImageMetadataReader).Assembly.GetName().Version + " · managed C#";

    public Dictionary<string, JsonElement> Read(string path, bool includeAllGroups = false)
    {
        var document = ReadDocument(path);
        if (includeAllGroups)
        {
            return document.Fields;
        }
        var fields = new Dictionary<string, JsonElement>(document.Fields.Count, StringComparer.Ordinal);
        foreach (var field in document.Fields)
        {
            var colon = field.Key.IndexOf(':');
            var name = field.Key[(colon + 1)..];
            // Thumbnail dimensions must not replace full-image dimensions.
            if (!field.Key.StartsWith("IFD1:", StringComparison.Ordinal))
            {
                fields.TryAdd(name, field.Value);
            }
        }
        return fields;
    }

    public byte[] Read(string path, string tag)
    {
        var document = ReadDocument(path);
        if (document.Payloads.TryGetValue(tag, out var exact))
        {
            return exact;
        }
        byte[]? found = null;
        foreach (var payload in document.Payloads)
        {
            var name = payload.Key.AsSpan(payload.Key.IndexOf(':') + 1);
            if (name.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                if (found != null)
                {
                    throw new EchoException("Multiple metadata fields have this name. Select the group-qualified field.", 2);
                }
                found = payload.Value;
            }
        }
        return found ?? throw new EchoException("No binary payload was found for " + tag + ".", 2);
    }

    private PhotoMetadata ReadDocument(string path)
    {
        try
        {
            var document = new PhotoMetadata();
            var jpeg = false;
            foreach (var directory in ImageMetadataReader.ReadMetadata(path))
            {
                MetadataFields.Add(directory, document);
                jpeg |= directory is JpegDirectory;
            }
            if (jpeg)
            {
                // This vendor APP7 block is separate from EXIF maker notes.
                foreach (var segment in JpegSegmentReader.ReadSegments(path, [JpegSegmentType.App7]))
                {
                    DjiDebugMetadata.Decode(segment.Bytes, document);
                }
            }
            return document;
        }
        catch (Exception error) when (error is ImageProcessingException or IOException or UnauthorizedAccessException)
        {
            throw new EchoException("Unable to read photo metadata: " + error.Message, 2);
        }
    }
}
