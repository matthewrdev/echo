using System.ComponentModel.Composition;
using System.Text;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Storage;

namespace Echo.Pipeline;

[Export(typeof(IAlignmentPreviewWriter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class AlignmentPreviewWriter : IAlignmentPreviewWriter
{
    private readonly IFileOperations files;
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public AlignmentPreviewWriter(IFileOperations files, IJsonCodec json)
    {
        this.files = files;
        this.json = json;
    }

    public void Write(string path, AlignmentPreview preview, CancellationToken cancellation)
    {
        var scene = preview.Scene;
        var header = new AlignmentPreviewHeader
        {
            TotalPoints = preview.TotalPoints,
            Scene = new ReconstructionScene
            {
                Frame = scene.Frame,
                Origin = scene.Origin,
                Cameras = scene.Cameras,
                Unplaced = scene.Unplaced,
                Warnings = scene.Warnings,
                Bundle = scene.Bundle
            }
        };
        var images = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in scene.Points)
        {
            cancellation.ThrowIfCancellationRequested();
            if (images.Add(point.ImageA))
            {
                header.Images.Add(point.ImageA);
            }
            if (images.Add(point.ImageB))
            {
                header.Images.Add(point.ImageB);
            }
        }
        var metadata = Encoding.UTF8.GetBytes(json.Encode(header));
        var codec = new CloudPointRecordCodec(header.Images);
        // A single atomic file keeps camera metadata and point records from the
        // same iteration, even when a viewer is reading a recycled preview slot.
        files.AtomicWrite(path, stream =>
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(0x31505345); // ESP1: sparse preview v1.
            writer.Write(metadata.Length);
            writer.Write(metadata);
            writer.Write(0x31504345); // Existing Echo record-buffer header.
            writer.Write(1);
            writer.Write(codec.Size);
            writer.Write(scene.Points.Count);
            var buffer = new byte[codec.Size * 4096];
            var count = 0;
            foreach (var point in scene.Points)
            {
                codec.Encode(point, buffer.AsSpan(count * codec.Size, codec.Size));
                count++;
                if (count == 4096)
                {
                    cancellation.ThrowIfCancellationRequested();
                    writer.Write(buffer);
                    count = 0;
                }
            }
            cancellation.ThrowIfCancellationRequested();
            writer.Write(buffer.AsSpan(0, count * codec.Size));
        });
    }
}
