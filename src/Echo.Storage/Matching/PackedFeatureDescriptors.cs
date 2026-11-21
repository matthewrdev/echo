namespace Echo.Storage.Matching;

internal sealed record PackedFeatureDescriptors(float[] Values, int[] PointOffsets, int[] LineOffsets);
