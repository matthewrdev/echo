namespace Echo.Validation;

public static class ValidationPaths
{
    public static string ContainedPath(string root, string relative)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fixture paths must remain inside their dataset.");
        }
        return path;
    }
}
