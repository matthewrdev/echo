namespace Echo.Storage.Pages;

internal readonly record struct HashIndexEntry<TKey, TValue>(
    TKey Key,
    TValue Value,
    int Next);
