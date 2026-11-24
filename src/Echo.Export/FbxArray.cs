namespace Echo.Export;

internal sealed record FbxArray(byte Type, int Count, int ElementSize, Action<BinaryWriter> Write);
