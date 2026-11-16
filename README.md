# Echo

**Turn photographs into 3D, with a clear view of every step.**

Echo is a native macOS photogrammetry workbench for reconstructing cliffs, terrain and other real-world subjects from overlapping photographs. Follow the reconstruction from image features and camera alignment through depth maps, point clouds and textured models.

## See what your scan is doing

- **Use your GPU.** Depth processing automatically prefers Metal on supported Macs, with CPU fallback.
- **Watch results arrive.** Inspect detected features and depth maps as each photograph finishes processing.
- **Understand the reconstruction.** Compare matches, overlay depth and confidence on the original photo, and inspect cameras and points in 3D.
- **Use your capture data.** Explore photo metadata, DJI camera orientation and GPS positioning alongside the reconstruction.
- **Iterate without starting over.** Reuse compatible checkpoints, resume interrupted work, rerun individual stages and manage generated artifacts separately from source photos.
- **Take the result with you.** Export coloured point clouds and textured GLB models, with georeference information when available.

Echo is in active development. Reconstruction quality is experimental; inspect the results before relying on their geometry or scale.

## Get started

To build from source, install Xcode and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then launch the desktop app:

```bash
./macos.command
```

Create a project, import overlapping JPEG, PNG or TIFF photographs, and choose a processing stage. Convert camera RAW files before importing. Use **Trim similar photos** to exclude redundant captures without deleting originals.

Right-click a recent project to rename it, remove it from the recent list, or permanently delete its folder from disk after confirmation. Renaming updates the saved project name and keeps its folder location. Projects with a saved mesh show a preview thumbnail, using a texture when available.

In **Mesh → Island trimming**, choose **Remove small islands** or **Keep main surface only** to remove disconnected geometry after hole closing and before texturing. Small-island size is measured as a percentage of the largest component's surface area, not its triangle count (default threshold: 0.1%). Trimming is off by default. Main-surface mode also removes legitimate detached objects; it does not remove protrusions still connected to the main surface. Start a new run from Mesh to apply a change while reusing fusion.

For earlier cleanup, **Fusion → Trim islands before meshing** removes disconnected observed volume before mesh extraction and hole closing. Choose small regions (default threshold: 0.1% of the largest region's fusion sample count) or the main region only. It is off by default and reuses the existing cloud when changed. Fusion regions can include nearby fragments joined by the integration band, so the later mesh pass is still useful.

**Texture → Reduce sky colour bleed** adds an optional skyline sampling margin (start at 8 source-photo pixels). It detects smooth sky-coloured regions connected to a photo's top edge and extends nearby foreground colour across a narrow band. It applies to baking, seam correction and inferred boundary colours without editing source photos. This heuristic can miss sky or misidentify pale objects; stronger margins can soften edges. It does not conceal large geometry/projection errors. Changing it rebuilds Texture and Export only.

The activity panel retains the full history for each stage. Scroll back to pause following live messages, or enable **Follow latest** to return to the newest entry. **CLI commands** shows the actual executable and quoted arguments launched by the desktop, with distinct terminal styling. Commands are saved in the project's `logs/desktop-commands.ndjson`; enable **Background queries** to include automatic status checks.

To build a standalone Release app:

```bash
./scripts/package-mac.sh
open dist/Echo.app
```

The standalone package includes the required runtime and processing tools. Pass `osx-x64` to package for Intel; the default is Apple Silicon. Local builds are ad-hoc signed, not notarised.

## Automate with the CLI

```bash
./scripts/echo-photo project init /tmp/cliff.echo --name "Cliff"
./scripts/echo-photo --project /tmp/cliff.echo images import /path/to/photos --set drone
./scripts/echo-photo --project /tmp/cliff.echo run start --target export --events ndjson
./scripts/echo-photo --project /tmp/cliff.echo export-gis --run latest --output /tmp/cliff-gis
```

Commands return JSON; `--events ndjson` streams progress. Use `--help` for commands and `doctor` to check dependencies. Resume preserves a run's original settings; start a new run to apply changes.

## Development

The desktop uses SwiftUI, AppKit and SceneKit/Metal. The CLI and processing subsystems are C#/.NET projects composed with MEF.

```bash
./scripts/build.sh         # Build and run C# and native tests
./scripts/check-format.sh  # Check formatting and code conventions
```

See [AGENTS.md](AGENTS.md) for code conventions, [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md) for licensing.
