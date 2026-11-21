namespace Echo.Compute;

public static class MetalShaderSource
{
    public static string WithoutDocumentation(string source)
    {
        // Documentation headers are outside the source used to compile or fingerprint a kernel.
        var offset = 0;
        while (source.AsSpan(offset).StartsWith("/// ", StringComparison.Ordinal))
        {
            var endOfLine = source.IndexOf('\n', offset);
            if (endOfLine < 0)
            {
                return "";
            }

            offset = endOfLine + 1;
        }

        return source[offset..];
    }
}
