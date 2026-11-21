using Echo.Models;

namespace Echo.Pipeline.Alignment;

public interface IAlignmentPreviewWriter
{
    void Write(string path, AlignmentPreview preview, CancellationToken cancellation);
}
