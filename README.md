# Echo

Echo is a native macOS photogrammetry app that turns overlapping photographs into textured 3D models.

- Inspect image features, matches, camera alignment, depth maps and point clouds.
- Watch reconstruction previews and review processing steps and timings.
- Use Metal acceleration on supported Macs, with automatic resource planning.
- Resume interrupted work and reuse compatible saved results.
- Export coloured point clouds, textured GLB models and georeferenced data for GIS tools.

Reconstruction quality is experimental. Inspect results before relying on their geometry or scale.

## Get started

Install Xcode and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run:

```bash
./macos.command
```

Create a project, import overlapping JPEG, PNG or TIFF photographs, and choose a processing stage. Convert camera RAW files before importing. Each stage's saved results remain available for inspection and reuse.

To build a standalone Release app:

```bash
./scripts/package-mac.sh
open dist/Echo.app
```

The package includes the processing runtime. Apple Silicon is the default; pass `osx-x64` to build for Intel. Local builds are ad-hoc signed, not notarised.

## Command line

```bash
./scripts/echo-photo project init /path/to/project.echo --name "Cliff"
./scripts/echo-photo --project /path/to/project.echo images import /path/to/photos
./scripts/echo-photo --project /path/to/project.echo run start --target export --events ndjson
```

Use `--help` for commands and `doctor` to check dependencies. Commands return JSON; `--events ndjson` streams progress. Resume keeps the run's reconstruction settings; start a new run to apply reconstruction changes.

## Development

The desktop uses SwiftUI, AppKit and SceneKit/Metal. The CLI and processing subsystems use C#/.NET with MEF composition.

```bash
./scripts/build.sh         # Build and run C# and native tests
./scripts/check-format.sh  # Check formatting and code conventions
```

See [AGENTS.md](AGENTS.md) for code conventions.

## Licensing

See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md). Optional image-classification features download a model and process photographs locally; the notices explain its sources, caching and licensing restrictions.
