namespace Echo.Geometry;

public static class FrustumOverlap
{
    public static double Score(CaptureFrustum leftView, CaptureFrustum rightView)
    {
        var tolerance = leftView.Uncertainty + rightView.Uncertainty;
        var separation = (leftView.Centre - rightView.Centre).Length;
        if (separation > leftView.Radius + rightView.Radius + tolerance)
        {
            return 0;
        }

        // Convex-pyramid separating-axis test. Position uncertainty expands intervals;
        // overlap is a candidate-selection prior, never proof of a shared surface.
        foreach (var axis in leftView.FaceNormals)
        {
            if (Separates(axis, leftView.Vertices, rightView.Vertices, tolerance))
            {
                return 0;
            }
        }

        foreach (var axis in rightView.FaceNormals)
        {
            if (Separates(axis, leftView.Vertices, rightView.Vertices, tolerance))
            {
                return 0;
            }
        }

        foreach (var leftEdge in leftView.EdgeDirections)
        {
            foreach (var rightEdge in rightView.EdgeDirections)
            {
                var axis = leftEdge.Cross(rightEdge);
                if (Separates(axis, leftView.Vertices, rightView.Vertices, tolerance))
                {
                    return 0;
                }
            }
        }

        // Symmetric sampling ranks overlap after the exact separating-axis rejection.
        // Grazing/uncertainty-only intersections retain a small, nonzero score.
        var sharedSamples = SharedSamples(leftView, rightView) + SharedSamples(rightView, leftView);
        return (1 + sharedSamples) / (1 + separation / Math.Min(leftView.Depth, rightView.Depth));
    }

    private static bool Separates(V3 axis, ReadOnlySpan<V3> leftVertices, ReadOnlySpan<V3> rightVertices, double tolerance)
    {
        var length = axis.Length;
        if (length < 1e-10)
        {
            return false;
        }

        axis /= length;
        var leftMinimum = double.PositiveInfinity;
        var leftMaximum = double.NegativeInfinity;
        var rightMinimum = double.PositiveInfinity;
        var rightMaximum = double.NegativeInfinity;
        foreach (var vertex in leftVertices)
        {
            var projection = vertex.Dot(axis);
            leftMinimum = Math.Min(leftMinimum, projection);
            leftMaximum = Math.Max(leftMaximum, projection);
        }

        foreach (var vertex in rightVertices)
        {
            var projection = vertex.Dot(axis);
            rightMinimum = Math.Min(rightMinimum, projection);
            rightMaximum = Math.Max(rightMaximum, projection);
        }

        return leftMinimum > rightMaximum + tolerance || rightMinimum > leftMaximum + tolerance;
    }

    private static int SharedSamples(CaptureFrustum sourceView, CaptureFrustum targetView)
    {
        var count = 0;
        for (var depthStep = 1; depthStep <= 3; depthStep++)
        {
            for (var horizontal = -1; horizontal <= 1; horizontal++)
            {
                for (var vertical = -1; vertical <= 1; vertical++)
                {
                    if (targetView.Contains(sourceView.Point(horizontal * .8, vertical * .8, sourceView.Depth * depthStep / 4)))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }
}
