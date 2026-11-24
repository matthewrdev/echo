namespace Echo.Export;

internal sealed record UsdzEntry(byte[] Name, uint Offset, uint Length, uint Crc);
