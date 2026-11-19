using Echo.Contracts;

namespace Echo.Pipeline;

public interface IAlignmentPreviewWriter
{
    void Write(string path, AlignmentPreview preview, CancellationToken cancellation);
}
