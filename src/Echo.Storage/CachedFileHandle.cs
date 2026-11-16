using Microsoft.Win32.SafeHandles;

namespace Echo.Storage;

internal sealed record CachedFileHandle(PageFile File, SafeFileHandle Handle);
