# Echo

**Turn photographs into 3D, with a clear view of every step.**

Echo is a native macOS photogrammetry workbench for reconstructing cliffs, terrain and other real-world subjects from overlapping photographs. Follow the reconstruction from image features and camera alignment through depth maps, point clouds and textured models.

## See what your scan is doing

- **Watch results arrive.** Inspect detected features and depth maps as each photograph finishes processing.
- **Understand the reconstruction.** Compare matches, overlay depth and confidence on the original photo, and inspect cameras and points in 3D.
- **Use your capture data.** Explore photo metadata, DJI camera orientation and GPS positioning alongside the reconstruction.
- **Iterate without starting over.** Reuse compatible checkpoints, resume interrupted work, rerun individual stages and manage generated artifacts separately from source photos.
- **Take the result with you.** Export coloured point clouds and textured GLB models, with georeference information when available.

Echo is in active development. Reconstruction quality is experimental; inspect the results before relying on their geometry or scale.

## Get started

Install Xcode, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [ExifTool](https://exiftool.org/) (`brew install exiftool`). Then launch the desktop app:

```bash
./scripts/desktop
```

Create a project, import overlapping JPEG, PNG or TIFF photographs, and choose a processing stage. Convert camera RAW files before importing.

To build a standalone Release app:

```bash
./scripts/package-mac.sh
open dist/Echo.app
```

The package includes the .NET runtime. ExifTool is still required for import. Pass `osx-x64` to package for Intel; the default is Apple Silicon. Local builds are ad-hoc signed, not notarised.

## Automate with the CLI

```bash
./scripts/echo-photo project init /tmp/cliff.echo --name "Cliff"
./scripts/echo-photo --project /tmp/cliff.echo images import /path/to/photos --set drone
./scripts/echo-photo --project /tmp/cliff.echo run start --target export --events ndjson
```

Commands return JSON; `--events ndjson` streams progress. Use `--help` for commands and `doctor` to check dependencies. Resume preserves a run's original settings; start a new run to apply changes.

## Development

The desktop uses SwiftUI, AppKit and SceneKit/Metal. Reconstruction runs on the CPU in focused C#/.NET projects, composed with MEF.

```bash
./scripts/build.sh         # Build and run C# and native tests
./scripts/check-format.sh  # Check formatting and code conventions
```

See [AGENTS.md](AGENTS.md) for code conventions and [LICENSE](LICENSE) for licensing.
