namespace Echo.Storage;

internal readonly record struct PageAddress(PageFile File, long Index);
