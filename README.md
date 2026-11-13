# Echo

A small, experimental, CLI-first photogrammetry workbench for macOS. The CLI and processing implementation are C#/.NET 10; the desktop is a native macOS SwiftUI/AppKit application. Each subsystem is a separate project. No existing Red-Point processing code or external photogrammetry engine is used.

This is a rough first pass: it can import photographs, use DJI GPS/gimbal metadata, reconstruct sparse points, estimate CPU stereo depth, fuse a dense cloud into connected geometry and export a GLB with an 8192 × 8192 texture and georeference sidecar. **The exported geometry is experimental and can be incomplete or misaligned.** This is not yet a replacement for a mature reconstruction engine.

## Run it

Install Xcode (for the native macOS app), the .NET 10 SDK and [ExifTool](https://exiftool.org/) (`brew install exiftool`). Then:

```bash
./scripts/build.sh
./scripts/echo-photo doctor
./scripts/echo-photo project init /tmp/my-cliff.echo --name "My cliff"
./scripts/echo-photo --project /tmp/my-cliff.echo images import /path/to/photos --set drone
./scripts/echo-photo --project /tmp/my-cliff.echo run start --target export --events ndjson
./scripts/echo-photo --project /tmp/my-cliff.echo inspect
./scripts/desktop --project /tmp/my-cliff.echo
```

The CLI's short responses are JSON; `--events ndjson` streams durable job events to stdout. Without it, job progress goes to stderr. `--help` lists commands. `scripts/echo-photo` builds when no binary exists; rerun `scripts/build.sh` after editing source.

The final run's `artifacts.export` field identifies a directory inside the project containing:

- `cliff.glb`: triangle mesh, UVs and embedded JPEG atlas.
- `cliff.geo.json`: local coordinate convention, WGS84 origin, row-major local-to-ECEF matrix, camera transforms and accuracy caveats.
- `cliff.report.json`: counts, texture dimensions, unplaced images and reconstruction limitations.

`./scripts/package-mac.sh` builds a self-contained Apple Silicon `dist/Echo.app`. Pass `osx-x64` for Intel. The local bundle is ad-hoc signed, not notarised; ExifTool is still needed for import. The desktop locates Homebrew ExifTool even when Finder has a limited PATH. `ECHO_EXIFTOOL` can point to another executable.

## Desktop

The native SwiftUI/AppKit app invokes CLI subprocesses through JSON/NDJSON. SceneKit uses Metal to display point clouds, camera frusta, captured image planes and meshes. Project edits and processing go through the CLI. The desktop can be rebuilt independently with `swift build --package-path macos`.

- Getting-started screen with New/Open and persistent recent projects.
- Stage tabs stay above the artifact viewer. A global working bar and per-stage readable logs stay below it. Historical runs remain available from the project clock icon.
- Scrolling thumbnail browsers for features, matched image pairs and depth maps. All matches are shown by default. Click correspondence endpoints to inspect matching scores and both image patches; scroll to zoom and drag to pan.
- Separate viewers for sparse cameras/cloud, depth/confidence, dense cloud, fused volume, mesh, texture and exported GLB. Dense construction publishes sampled live previews; the complete point cloud is saved separately.
- Every stage has settings, also reachable through the project cog. Controls affect real CLI settings. The editor compares the next run with the viewed run and warns which dependent stages need rebuilding. Out-of-date saved results are marked in the tabs and viewer.
- Photo thumbnails are grouped under collapsible set headings. Hover for metadata; double-click for the original photograph and all available EXIF. Values have copy actions. DJI flight fields are grouped; opaque private binary fields are clearly labelled instead of displayed as garbled text.
- All maps default to satellite, with standard and offline-grid alternatives. Wheel scrolling zooms and dragging pans. Camera frustums are coloured by vertical tilt, with a numeric legend. Missing attitude is not invented.
- GPS coordinates are visible and copyable in the photo inspector; Show on map focuses the selected photograph. Georeference and placement hints are available from the project panel.
- Name/camera, GPS, unplaced and disabled-image filters affect viewing only. Changing processing selection or hints warns before invalidating dependent outputs.
- Texture opens on the textured model. Atlas view displays connected mesh charts, with surface-projected photographic pixels, split seam vertices, rotated silhouette packing and padded borders. The native viewer and GLB use the same top-left UV convention.

## Independent C# projects

| Project | Responsibility |
| --- | --- |
| `Echo.Contracts` | Versioned project, run and artifact DTOs, one type per file |
| `Echo.Infrastructure` | Injected JSON serialization and atomic file operations |
| `Echo.Composition` | MEF composition root and service lifetime ownership |
| `Echo.Metadata` | EXIF/XMP import through ExifTool |
| `Echo.Imaging` | Image decoding, orientation, area downsampling and image encoding |
| `Echo.Projects` | Project revisions, copied originals, image sets, hints and file locks |
| `Echo.Geometry` | WGS84/local transforms, camera projection, triangulation and essential-matrix estimation |
| `Echo.Features` | Harris corners and normalised patch descriptors |
| `Echo.Matching` | Mutual nearest-neighbour descriptor matching |
| `Echo.Reconstruction` | Metadata initialisation, limited orientation refinement, visual seed and camera registration |
| `Echo.Optimization` | Robust joint camera, point and focal-length bundle adjustment |
| `Echo.Depth` | Coarse-to-fine CPU stereo with warped patch correlation |
| `Echo.DenseCloud` | Multiview depth validation, coloured cloud and binary PLY |
| `Echo.Fusion` | Projective signed-distance surface fusion |
| `Echo.Meshing` | Surface nets and visibility-tested source camera assignment |
| `Echo.Simplification` | Quadric edge collapse with topology and boundary checks |
| `Echo.Texturing` | Connected mesh charts, UV packing, perspective-correct photo baking and padding |
| `Echo.Export` | GLB writer and structural GLB inspection |
| `Echo.Pipeline` | Stage dependencies, fingerprints, snapshots, events, cancellation and resume |
| `Echo.Cli` | Scriptable command interface and exit codes |
| `Echo.Tests` | Executable numerical and end-to-end integration checks |

Build any assembly independently with `dotnet build src/Echo.Geometry`, for example. Services export interfaces through MEF and receive dependencies through explicit importing constructors. Pipeline stages and CLI command handlers are independently composed. Dependencies flow through project references; the CLI and desktop are separate entry points. The only algorithm libraries are MathNet.Numerics for linear algebra and SkiaSharp for pixels/codecs. See [architecture](docs/architecture.md) for file contracts and implementation limits.

## Resume and edit

```bash
./scripts/echo-photo --project /tmp/my-cliff.echo run cancel latest
./scripts/echo-photo --project /tmp/my-cliff.echo run status latest
./scripts/echo-photo --project /tmp/my-cliff.echo run resume RUN_ID --events ndjson
./scripts/echo-photo --project /tmp/my-cliff.echo config show > settings.json
# Edit settings.json, for example textureSize or depthWidth:
./scripts/echo-photo --project /tmp/my-cliff.echo config import settings.json
./scripts/echo-photo --project /tmp/my-cliff.echo run start --target export --events ndjson
```

`resume` uses the original frozen inputs, even if the project has since changed. Start a new run to apply edits. Valid matching artifacts are shared across runs; a texture-size edit reuses geometry. Feature extraction, matching and depth commit per image/pair, so interrupted stages retain their completed units. An interrupted individual image/pair is recomputed. Alignment retains inspectable scene snapshots but restarts its solve if interrupted. One run writes a project at a time.

## Work and detail settings

All controls are serialized in `project.json` and accepted by `config import`. Settings are frozen into each run.

| Stage | Controls |
| --- | --- |
| Features | `featureWidth`, `featureLimit` |
| Matching | `matchAllPairs`, `pairWindow`, `gpsPairNeighbours`, `matchRatioThreshold`, `matchDistanceThreshold` |
| Alignment | `bundleIterations` |
| Depth | `depthWidth`, `depthPlanes`, `depthRefinementSamples`, `depthNeighbours`, `depthNear`, `depthFar` |
| Dense cloud | `cloudNeighbours`, `cloudMinimumSupport`, `cloudDepthTolerance` |
| Fusion | `fusionDetailMultiplier` |
| Mesh | `faceLimit` (also informs fusion's grid budget) |
| Texture | `textureSize`, `textureChartFaces`, `textureChartAngle`, `textureGutter`, `textureJpegQuality` |
| Export | `exportValidate` |

More work does not guarantee better reconstruction. A larger depth map increases possible point density; the distance range and sample count control the depth search. Higher matching thresholds admit more candidates and more false matches. The matching viewer displays all saved candidates; geometric verification happens in alignment. New matching artifacts include rejection counts. Resume uses saved settings; start a new run to apply edits. Existing artifacts remain immutable for inspection, while input fingerprints prevent incompatible reuse.

Settings provide sliders and exact numeric entry. Depth maps can use up to 8192 pixels (capped to source width), 2048 initial search samples, 128 refinement samples and 32 stereo neighbours. Feature analysis supports up to 8192 pixels and 20,000 keypoints. These are limits, not accuracy guarantees; 1024-pixel depth is a preview setting for 4K photographs. The current detector is point-based; there is no line-segment alignment solver.

To force fresh processing, use **More actions → Reset from this stage…**, then **Process**. The warning names all affected later stages. Reset adds a new cache revision for those stages and their per-image/per-pair checkpoints; it preserves photographs, earlier checkpoints, and every saved run. Resume still uses the saved run's original inputs. This does not reclaim disk space.

```bash
./scripts/echo-photo --project /tmp/my-cliff.echo pipeline reset --from depth
./scripts/echo-photo --project /tmp/my-cliff.echo run --target export
```

## Hints and mixed cameras

```bash
./scripts/echo-photo --project /tmp/my-cliff.echo images import /path/to/mirrorless-jpegs --set ground
./scripts/echo-photo --project /tmp/my-cliff.echo images list --set ground
./scripts/echo-photo --project /tmp/my-cliff.echo hints overlap IMAGE_A IMAGE_B
./scripts/echo-photo --project /tmp/my-cliff.echo hints tie IMAGE_A IMAGE_B --ax 101 --ay 225 --bx 382 --by 210
./scripts/echo-photo --project /tmp/my-cliff.echo hints camera IMAGE_A --lat -33.5 --lon 150.2 --alt 100 --yaw 45 --pitch -20 --roll 0
```

Tie coordinates are in the orientation-normalised, full-resolution image. Yaw is clockwise from true north, pitch is positive upwards and roll follows the documented camera convention. Camera hints override metadata; they are initialisation constraints, not survey control points. Overlap hints add candidate pairs. Tie points add explicit matching observations.

JPEG, PNG and TIFF are supported; convert camera RAW files first. Import retains originals and makes normalised JPEG working copies. Optional `photo.jpg.echo.json` sidecars can supply `gps`, `attitude` and `focalPixels` for a calibrated input. Images without GPS can register against existing 2D–3D observations, or seed an unreferenced reconstruction. The simple descriptor is sensitive to scale, rotation, exposure and viewpoint changes, so mixed drone/mirrorless registration remains experimental.

## Current boundaries

- Joint camera/point/focal refinement uses GPS as a loose position prior and fixes the first camera as a gauge. There is no lens-distortion solve, rolling-shutter model or survey-control optimisation.
- DJI gimbal fields are used when present. We do not substitute aircraft heading for camera heading. Missing attitude remains missing until supplied or visually estimated.
- Local GLB axes are **X east, Y up, Z south** when georeferenced. Altitude datum is often unknown. With insufficient georeference metadata, scale and frame are arbitrary; the output explicitly says so.
- Depth maps produce a separately saved, multiview-validated dense cloud. TSDF fusion and surface nets produce shared surface geometry. Unknown regions remain open; vegetation can produce isolated fragments. No seam blending or watertightness guarantee.
- Defaults target about **1.5 million triangles**, 1024-pixel depth maps and an **8192 × 8192** photographic atlas. A target does not manufacture detail: output may be smaller where observations are insufficient. Texture resolution is shared across surface charts.
- This version has been exercised on a small real photo pack; 500+ photo quality, runtime and memory are not validated. Reconstruction runs on the CPU; the native viewer renders with Metal.
- `compare-glb` reports structure, texture sizes and accessor bounds. It does not register two coordinate frames or measure geometric accuracy.

See [validation notes](docs/validation.md) for the measured first-pass result and test commands.

## Source conventions

See [AGENTS.md](AGENTS.md) and `.editorconfig`. `scripts/check-format.sh` verifies formatting, one named type per file, explicit class constructors, field naming and the absence of static services. Swift uses `.swift-format`. Run `scripts/build.sh` for the C# integration checks, native build and Swift contract tests.
