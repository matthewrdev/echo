# Metadata dependencies

Echo reads metadata inside its C# process using [MetadataExtractor 2.9.3](https://github.com/drewnoakes/metadata-extractor-dotnet) and its [XmpCore](https://github.com/drewnoakes/xmp-core-dotnet) dependency. MetadataExtractor uses Apache License 2.0; XmpCore uses the BSD license. Copyright notices and the license are included in `licenses/` and in the packaged app's Resources directory.

DJI debug payloads are retained as original bytes. Their private histogram/debug layouts are not assumed to be documented numeric arrays.

Other .NET dependencies retain their upstream licenses; see the project files and NuGet package metadata for versions and terms.
