namespace Echo.Geometry;

public sealed record SimilarityTransform(double Scale, double[] Rotation, V3 Translation)
{
    public V3 Apply(V3 position) => CameraGeometry.Rotate(Rotation, position) * Scale + Translation;
}
