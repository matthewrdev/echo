namespace Echo.Storage;

public readonly record struct HeapEntry<T>(T Value, double Priority);
