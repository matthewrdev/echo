using Echo.Contracts;

namespace Echo.Pipeline;

public interface IAlignmentPreviewReader
{
    AlignmentPreview Read(string path, bool summary);
}
