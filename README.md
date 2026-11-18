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

The package includes the processing runtime. Apple Silicon is the default; pass `osx-x64` to build for Intel. Packaging uses `Developer ID Application: alpha outdoors pty. ltd. (65MZ7WPY52)` for distribution outside the Mac App Store. Install that certificate and its private key in Keychain before packaging. Set `CODESIGN_IDENTITY` to another Developer ID Application certificate name or SHA-1, or `-` for an ad-hoc local build.

To notarise and create a distribution ZIP, store credentials interactively in Keychain once, then use that profile when packaging:

```bash
xcrun notarytool store-credentials echo-notary
NOTARYTOOL_PROFILE=echo-notary ./scripts/package-mac.sh
```

Use the signing certificate's team when creating the profile. Notarisation staples Apple's ticket to the app and creates `dist/Echo.zip`. Without `NOTARYTOOL_PROFILE`, the app is signed but not notarised. Run either packaging script with `--help` for details.

## Command line

```bash
./scripts/echo-photo project init /path/to/project.echo --name "Cliff"
./scripts/echo-photo --project /path/to/project.echo images import /path/to/photos
./scripts/echo-photo --project /path/to/project.echo run start --target export --events ndjson
```

Use `--help` for commands and `doctor` to check dependencies. Commands return JSON; `--events ndjson` streams progress. Resume keeps the run's reconstruction settings; start a new run to apply reconstruction changes.

Export a geographically aligned run with `./scripts/echo-photo --project /path/to/project.echo export-gis --run latest --output /path/to/new-folder`, or use **Georeference → Export geographic data** in the desktop. The bundle includes GeoJSON, GeoPackage and CSV, plus a Redpoint calibration when a saved GLB is available. Known WGS84 ellipsoidal heights also enable 3D Tiles and LAS point clouds. Desktop exports include a GeoTIFF model projection. The export manifest explains unavailable outputs; `QGIS.txt` contains import instructions. In Redpoint's contributor portal, load the matching GLB and use **Location → Import Echo georeference**, then save the scene.

Colour tuning and texture seam softening default to strength `0.5` and a `4`-pixel atlas strip. Add `--colour-tuning off --edge-softening off` to `run start` to disable them, or supply a strength from `0` to `1` and a width from `0` to `16`. These overrides affect only that run; project settings stay unchanged.

Subject distance defaults to automatic: the camera footprint and reliable foreground observations limit distant background reconstruction. Use `run start --subject-distance auto`, `--subject-distance off`, or a positive maximum camera depth in metres such as `--subject-distance 500`. The same controls are in the desktop's Depth settings. Explicit near/far depth bounds take precedence over the automatic subject limit; a custom subject limit still caps those bounds. CLI overrides apply only to the new run, and the chosen automatic distance is recorded in the depth artifact and activity log.

## Development

The desktop uses SwiftUI, AppKit and SceneKit/Metal. The CLI and processing subsystems use C#/.NET with MEF composition.

```bash
./scripts/build.sh         # Build and run C# and native tests
./scripts/check-format.sh  # Check formatting and code conventions
```

See [AGENTS.md](AGENTS.md) for code conventions.

## Licensing

See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md). Optional image-classification features download a model and process photographs locally; the notices explain its sources, caching and licensing restrictions.
