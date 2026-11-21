namespace Echo.Storage.Pages;

internal readonly record struct PageAddress(
    PageFile File,
    long Index);
