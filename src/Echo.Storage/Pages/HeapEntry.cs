namespace Echo.Storage.Pages;

public readonly record struct HeapEntry<T>(
    T Value,
    double Priority);
