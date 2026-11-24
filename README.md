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

Create a project, import overlapping JPEG, PNG, TIFF, HEIC or HEIF photographs, and choose a processing stage. On macOS, Echo imports HEIC and HEIF directly, keeping the originals and their metadata alongside the JPEGs used for processing. Convert camera RAW files before importing. Each stage's saved results remain available for inspection and reuse.

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

Use `./scripts/echo` from a source checkout or `/Applications/Echo.app/Contents/MacOS/echo` from the installed app. Use the executable path in scripts because shells usually provide a built-in `echo`.

```bash
./scripts/echo project init /path/to/project.echo --name "Cliff"
./scripts/echo --project /path/to/project.echo images import /path/to/photos
./scripts/echo --project /path/to/project.echo run start --target export --events ndjson
```

Use `--help` for commands and `doctor` to check dependencies. Start a run with `--settings FILE` to use a JSON settings file without editing the project defaults. Resume keeps the run's saved inputs and settings. See the [CLI scripting reference](src/Echo.Cli/README.md) for JSON and progress formats.

Use **Export saved run** in the desktop toolbar or `./scripts/echo export --help` for mesh and point-cloud exports. **Survey export** supports local coordinates, Earth-centred WGS84, or a projected EPSG code. LAZ, COPC, E57, and projected CRS exports require a local PDAL installation. Echo checks the standard Homebrew locations and `PATH`, and `ECHO_PDAL` can specify an executable. Run `./scripts/echo doctor` to check available PDAL writers. **3MF print export** scales a closed mesh to the requested longest side in millimetres.

```bash
./scripts/echo --project cliff.echo export cloud --format laz --crs EPSG:7856 --output survey
./scripts/echo --project cliff.echo export mesh --format 3mf --size-mm 150 --output print
./scripts/echo --project cliff.echo export images --output source-photos
```

Export a geographically aligned run with `./scripts/echo --project /path/to/project.echo export-gis --run latest --output /path/to/new-folder`, or use **Georeference → Export geographic data** in the desktop. The bundle includes GeoJSON, GeoPackage and CSV, plus a Redpoint calibration when a saved GLB is available. Known WGS84 ellipsoidal heights also enable 3D Tiles and LAS point clouds. On macOS, exports include a GeoTIFF model projection when a saved mesh is available. The export manifest explains unavailable outputs; `QGIS.txt` contains import instructions. In Redpoint's contributor portal, load the matching GLB and use **Location → Import Echo georeference**, then save the scene.

Use **Georeference → Test GPS** to enter decimal latitude/longitude or pick a location on the map, then inspect its marker and horizontal accuracy radius on the model. Clicking the model reads back coordinates for copying. With altitude blank, Echo uses the topmost intersecting surface and reports multiple intersections; an entered altitude must use the model’s known altitude reference. The probe checks coordinate placement without changing calibration or independently establishing GPS accuracy.

In the 3D crop editor, use **Paint crop** to draw a convex outline that projects through the model from the current view. New painted, box and cylinder regions default to **Include**; switch to **Exclude** for cutouts. Save the regions and process again from Fusion to apply them while reusing the full cloud. Escape cancels a stroke and **Undo last paint** removes the latest painted region. Dragging a mesh or Option-dragging while editing orbits around the surface point under the pointer; empty space retains the current pivot.


Colour tuning and texture seam softening default to strength `0.5` and a `4`-pixel atlas strip. Add `--colour-tuning off --edge-softening off` to `run start` to disable them, or supply a strength from `0` to `1` and a width from `0` to `16`. These overrides affect only that run; project settings stay unchanged.

Subject distance defaults to automatic: the camera footprint and reliable foreground observations limit distant background reconstruction. Use `run start --subject-distance auto`, `--subject-distance off`, or a positive maximum camera depth in metres such as `--subject-distance 500`. The same controls are in the desktop's Depth settings. Explicit near/far depth bounds take precedence over the automatic subject limit; a custom subject limit still caps those bounds. CLI overrides apply only to the new run, and the chosen automatic distance is recorded in the depth artifact and activity log.

## Development

The desktop uses SwiftUI, AppKit and SceneKit/Metal. The CLI and processing subsystems use C#/.NET with explicit service registration at their entry points.

```bash
./scripts/build.sh         # Build and run C# and native tests
./scripts/check-format.sh  # Check formatting and code conventions
```

With the reference photo catalogue available locally, run `./scripts/check-reconstruction.sh /path/to/3d-assets /path/to/new-results all` to reconstruct the pinned Can Lolo, Northern Buttress and Je T’aime photo sets from scratch. Each case records quality checks, main-hull topology, timing and sampled peak memory. Use `--memory-mib` to test a resource budget, and `--max-seconds` or `--max-peak-mib` to enforce performance limits. The results directory must be new; reference datasets are not bundled with the repository.

See [AGENTS.md](AGENTS.md) for code conventions.

## Licensing

See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md). Sky suppression and vegetation handling are enabled by default. Image classification downloads a model on first use and processes photographs locally; the notices explain its sources, caching and licensing restrictions. These controls can be disabled in processing settings.
