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

To build from source, install Xcode and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then launch the desktop app:

```bash
./scripts/desktop
```

Create a project, import overlapping JPEG, PNG or TIFF photographs, and choose a processing stage. Convert camera RAW files before importing.

To build a standalone Release app:

```bash
./scripts/package-mac.sh
open dist/Echo.app
```

The package includes .NET and reads photo metadata directly in C#. Users do not need Homebrew, ExifTool, Perl or any separate runtime installation. Pass `osx-x64` to package for Intel; the default is Apple Silicon. Local builds are ad-hoc signed, not notarised.

## Automate with the CLI

```bash
./scripts/echo-photo project init /tmp/cliff.echo --name "Cliff"
./scripts/echo-photo --project /tmp/cliff.echo images import /path/to/photos --set drone
./scripts/echo-photo --project /tmp/cliff.echo run start --target export --events ndjson
```

Commands return JSON; `--events ndjson` streams progress. Use `--help` for commands and `doctor` to check dependencies. Resume preserves a run's original settings; start a new run to apply changes.

Depth scheduling defaults to automatic memory budgeting (`depthMemoryBudgetMb: 0`). At the start of the depth stage, it budgets 25% of the runtime's memory capacity, bounded to 256 MB–32 GB, while retaining the configured photo and CPU concurrency limits. This is a scheduling estimate, not reserved memory or a hard process limit. In the desktop Depth settings, turn off **Automatic memory budget** to choose a fixed 256–32768 MB budget. Existing projects retain their explicitly saved budgets.

## Development

The desktop uses SwiftUI, AppKit and SceneKit/Metal. Reconstruction runs on the CPU in focused C#/.NET projects, composed with MEF.

```bash
./scripts/build.sh         # Build and run C# and native tests
./scripts/check-format.sh  # Check formatting and code conventions
```

See [AGENTS.md](AGENTS.md) for code conventions, [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md) for licensing.

Use **Trim similar photos** above the photo list to keep one sharp representative of near-identical shots. Extras are marked **Discarded**, with the retained photo identified, and excluded from processing. Originals are not deleted. Choose **Discarded**, select a photo and click **Restore photo** to bring it back; restored photos are protected from future trimming. Press **Process** afterwards: **Resume** intentionally preserves the original run's inputs. Photos referenced by enabled placement/tie/overlap hints are retained.

The CLI previews groups with `images deduplicate` and applies them with `images deduplicate --apply`. Use `images restore IMAGE_ID` to restore a discarded photo. Selection groups shots taken within 10 seconds, within 0.75 m and with camera orientation within 1 degree and matching focal length. A coarse visual check rejects changed content. Each excluded photo must match its retained representative directly, so gradual camera motion cannot chain into one group. It is conservative about viewpoint changes and uses stricter image similarity when GPS is absent. Sharpness is measured at preview resolution. Saved runs and source files remain intact.
