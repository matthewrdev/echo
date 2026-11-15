# Metadata dependencies

Echo reads metadata inside its C# process using [MetadataExtractor 2.9.3](https://github.com/drewnoakes/metadata-extractor-dotnet) and its [XmpCore](https://github.com/drewnoakes/xmp-core-dotnet) dependency. MetadataExtractor uses Apache License 2.0; XmpCore uses the BSD license. Copyright notices and the license are included in `licenses/` and in the packaged app's Resources directory.

DJI debug payloads are retained as original bytes. Their private histogram/debug layouts are not assumed to be documented numeric arrays.

Other .NET dependencies retain their upstream licenses; see the project files and NuGet package metadata for versions and terms.

# Screened Poisson surface reconstruction

Echo's C# surface-completion subsystem runs a bundled, isolated native worker built from [PoissonRecon](https://github.com/mkazhdan/PoissonRecon), version 18.76, commit `262b0f539d404057d1f36e1adc07fc9388678899`. The repository MIT license is included in `licenses/PoissonRecon-MIT.txt`; the source copyright and redistribution notices are reproduced in `licenses/PoissonRecon-source-notices.txt` and retained in the vendored headers. Both are included in the packaged app's Resources directory. No external installation or runtime download is required. The worker uses double precision and serial execution, with upstream `SANITIZED_PR` synchronization enabled.
