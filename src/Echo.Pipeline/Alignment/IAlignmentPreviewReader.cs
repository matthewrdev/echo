using Echo.Models;

namespace Echo.Pipeline.Alignment;

public interface IAlignmentPreviewReader
{
    AlignmentPreview Read(string path, bool summary);
}
