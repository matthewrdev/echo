using Microsoft.Win32.SafeHandles;

namespace Echo.Storage.Pages;

internal sealed record CachedFileHandle(
    PageFile File,
    SafeFileHandle Handle);
