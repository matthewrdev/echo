namespace Echo.Geometry;

public static class RandomSampleSelector
{
    public static void Select<T>(IReadOnlyList<T> source, Random random, Span<T> sample)
    {
        if (sample.Length > source.Count)
        {
            throw new ArgumentException("The sample cannot exceed the source collection.", nameof(sample));
        }

        Span<int> keys = sample.Length <= 128 ? stackalloc int[sample.Length] : new int[sample.Length];
        var filled = 0;
        for (var index = 0; index < source.Count; index++)
        {
            // Preserve the random draw count and stable tie order of OrderBy(random.Next).
            var key = random.Next();
            var position = filled;
            while (position > 0 && key < keys[position - 1])
            {
                position--;
            }

            if (position >= sample.Length)
            {
                continue;
            }

            for (var move = Math.Min(filled, sample.Length - 1); move > position; move--)
            {
                keys[move] = keys[move - 1];
                sample[move] = sample[move - 1];
            }

            keys[position] = key;
            sample[position] = source[index];
            filled = Math.Min(sample.Length, filled + 1);
        }
    }
}
