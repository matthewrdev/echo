namespace Echo.Storage;

internal sealed record PackedFeatureDescriptors(float[] Values, int[] PointOffsets, int[] LineOffsets);
