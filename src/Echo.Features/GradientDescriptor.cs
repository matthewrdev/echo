namespace Echo.Features;

public static class GradientDescriptor
{
    public static int Orientations(ReadOnlySpan<float> pixels, int width, int height, ScaleKeypoint point, Span<double> angles)
    {
        Span<double> histogram = stackalloc double[36];
        Span<double> smoothed = stackalloc double[36];
        histogram.Clear();
        var sigma = 1.5 * point.Scale;
        var radius = (int)Math.Round(3 * sigma);
        var centreX = (int)Math.Round(point.X);
        var centreY = (int)Math.Round(point.Y);
        for (var y = Math.Max(1, centreY - radius); y <= Math.Min(height - 2, centreY + radius); y++)
        {
            for (var x = Math.Max(1, centreX - radius); x <= Math.Min(width - 2, centreX + radius); x++)
            {
                var pixel = y * width + x;
                var gx = pixels[pixel + 1] - pixels[pixel - 1];
                var gy = pixels[pixel + width] - pixels[pixel - width];
                var dx = x - point.X;
                var dy = y - point.Y;
                var weight = Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma)) * Math.Sqrt(gx * gx + gy * gy);
                var bin = (int)Math.Round((Math.Atan2(gy, gx) + Math.PI) * 36 / (2 * Math.PI)) % 36;
                histogram[bin] += weight;
            }
        }

        for (var pass = 0; pass < 6; pass++)
        {
            for (var bin = 0; bin < 36; bin++)
            {
                smoothed[bin] = (histogram[(bin + 35) % 36] + histogram[bin] + histogram[(bin + 1) % 36]) / 3;
            }

            smoothed.CopyTo(histogram);
        }

        double maximum = 0;
        foreach (var value in histogram)
        {
            maximum = Math.Max(maximum, value);
        }

        var count = 0;
        for (var bin = 0; bin < 36 && count < angles.Length; bin++)
        {
            var left = histogram[(bin + 35) % 36];
            var centre = histogram[bin];
            var right = histogram[(bin + 1) % 36];
            var peak = centre > left && centre > right && centre >= maximum * .8;
            if (peak)
            {
                var offset = .5 * (left - right) / (left - 2 * centre + right);
                angles[count++] = (bin + offset) * (2 * Math.PI) / 36 - Math.PI;
            }
        }

        return count;
    }

    public static bool Describe(ReadOnlySpan<float> pixels, int width, int height, ScaleKeypoint point, double angle, Span<float> descriptor)
    {
        descriptor.Clear();
        var cellSize = 3 * point.Scale;
        var radius = (int)Math.Round(cellSize * Math.Sqrt(2) * 2.5);
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var centreX = (int)Math.Round(point.X);
        var centreY = (int)Math.Round(point.Y);
        for (var y = Math.Max(1, centreY - radius); y <= Math.Min(height - 2, centreY + radius); y++)
        {
            for (var x = Math.Max(1, centreX - radius); x <= Math.Min(width - 2, centreX + radius); x++)
            {
                var dx = x - point.X;
                var dy = y - point.Y;
                var rotatedX = (cosine * dx + sine * dy) / cellSize;
                var rotatedY = (-sine * dx + cosine * dy) / cellSize;
                var columnBin = rotatedX + 1.5;
                var rowBin = rotatedY + 1.5;
                var outside = columnBin <= -1 || columnBin >= 4 || rowBin <= -1 || rowBin >= 4;
                if (outside)
                {
                    continue;
                }

                var pixel = y * width + x;
                var gx = pixels[pixel + 1] - pixels[pixel - 1];
                var gy = pixels[pixel + width] - pixels[pixel - width];
                var orientationBin = (Math.Atan2(gy, gx) - angle) * 8 / (2 * Math.PI);
                orientationBin -= Math.Floor(orientationBin / 8) * 8;
                var weight = Math.Exp(-(rotatedX * rotatedX + rotatedY * rotatedY) / 8) * Math.Sqrt(gx * gx + gy * gy);
                Add(descriptor, columnBin, rowBin, orientationBin, weight);
            }
        }

        if (!Normalize(descriptor))
        {
            return false;
        }

        for (var index = 0; index < descriptor.Length; index++)
        {
            descriptor[index] = Math.Min(descriptor[index], .2f);
        }

        return Normalize(descriptor);
    }

    private static void Add(Span<float> descriptor, double columnBin, double rowBin, double orientationBin, double magnitude)
    {
        var column = (int)Math.Floor(columnBin);
        var row = (int)Math.Floor(rowBin);
        var orientation = (int)Math.Floor(orientationBin);
        var columnFraction = columnBin - column;
        var rowFraction = rowBin - row;
        var orientationFraction = orientationBin - orientation;
        for (var rowOffset = 0; rowOffset <= 1; rowOffset++)
        {
            var targetRow = row + rowOffset;
            if (targetRow is < 0 or >= 4)
            {
                continue;
            }

            var rowWeight = magnitude * (rowOffset == 0 ? 1 - rowFraction : rowFraction);
            for (var columnOffset = 0; columnOffset <= 1; columnOffset++)
            {
                var targetColumn = column + columnOffset;
                if (targetColumn is < 0 or >= 4)
                {
                    continue;
                }

                var weight = rowWeight * (columnOffset == 0 ? 1 - columnFraction : columnFraction);
                var offset = (targetRow * 4 + targetColumn) * 8;
                descriptor[offset + orientation % 8] += (float)(weight * (1 - orientationFraction));
                descriptor[offset + (orientation + 1) % 8] += (float)(weight * orientationFraction);
            }
        }
    }

    private static bool Normalize(Span<float> descriptor)
    {
        double squared = 0;
        foreach (var value in descriptor)
        {
            squared += value * value;
        }

        if (squared < 1e-16)
        {
            return false;
        }

        var inverse = (float)(1 / Math.Sqrt(squared));
        for (var index = 0; index < descriptor.Length; index++)
        {
            descriptor[index] *= inverse;
        }

        return true;
    }
}
