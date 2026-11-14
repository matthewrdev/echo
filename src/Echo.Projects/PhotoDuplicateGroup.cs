using Echo.Contracts;

namespace Echo.Projects;

public sealed record PhotoDuplicateGroup(Photo Keep, IReadOnlyList<Photo> Duplicates);
