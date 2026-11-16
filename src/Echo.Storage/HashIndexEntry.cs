namespace Echo.Storage;

internal readonly record struct HashIndexEntry<TKey, TValue>(TKey Key, TValue Value, int Next);
