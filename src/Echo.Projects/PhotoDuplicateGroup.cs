using Echo.Models;

namespace Echo.Projects;

public sealed record PhotoDuplicateGroup(Photo Keep, IReadOnlyList<Photo> Duplicates);
