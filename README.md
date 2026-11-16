# Echo

**Turn photographs into 3D, with a clear view of every step.**

Echo is a native macOS photogrammetry workbench for reconstructing cliffs, terrain and other real-world subjects from overlapping photographs. Follow the reconstruction from image features and camera alignment through depth maps, point clouds and textured models.

## See what your scan is doing

- **Use your GPU.** Eligible photogrammetry operations automatically use Metal on supported Macs, with CPU fallback.
- **Watch results arrive.** Inspect detected features and depth maps as each photograph finishes processing.
- **Understand the reconstruction.** Compare matches, overlay depth and confidence on the original photo, and inspect cameras and points in 3D.
- **Use your capture data.** Explore photo metadata, DJI camera orientation and GPS positioning alongside the reconstruction.
- **Iterate without starting over.** Reuse compatible checkpoints, resume interrupted work, rerun individual stages and manage generated artifacts separately from source photos.
- **Take the result with you.** Export coloured point clouds and textured GLB models, with georeference information when available.
- **Check geographic placement.** Choose **View on map** on a saved stage, or **Georeference** in the sidebar, to compare the model with Apple Maps satellite imagery and camera directions. Adjust the model overlay opacity, or use **3D** to orbit the model above a flat map surface. The map display height is a visual aid, not a terrain measurement. The top-right expand button fills the app window; the adjacent buttons export or close the viewer.
- **Export for QGIS.** Use the geographic viewer's export icon and choose a new folder. Load `model-map.png` with its world file and CRS sidecars for the textured top-down model projection, and `model.geojson` / `cameras.geojson` for WGS84 geometry. `photo-locations.geojson` includes every located source photo, its original altitude, takeoff-relative height when available, and reference evidence. `vertices.csv` and `georeference.json` retain local XYZ, connectivity references and the geographic transform. GeoJSON includes Z only for identified WGS84 ellipsoidal heights; other heights remain attributes. EXIF 3.x can identify the altitude reference; older ambiguous tags remain explained as unknown. Placement hints accept an explicit altitude reference (CLI: `--alt-datum`). See the exported `QGIS.txt` for import instructions. Apple basemap imagery is not included in the export.

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

**Depth maps → Exclude sky from depth** is opt-in. It uses a pretrained model to identify clear and cloudy sky, removing confident sky pixels from saved depth and confidence maps before previews and cloud processing. Mixed skyline boundaries are preserved, and cliff recovery cannot refill masked sky. Changing this setting rebuilds Depth and subsequent stages. Existing saved runs and explicitly saved settings remain available; start a new run to apply changes.

Sky exclusion, **Recover missing cliff detail** and **Optimise vegetation** share an optional 228 MB UPerNet model, downloaded from the [JElvis repository on Hugging Face](https://huggingface.co/JElvis/upernet-convnext-tiny-ade-onnx). Hugging Face hosts the file; ONNX Runtime processes photographs locally. All three features are off for new projects and for settings missing these fields. Previously saved enabled settings remain enabled.

On macOS, the verified model is cached by checksum in `~/Library/Caches/Echo/models/` and shared across projects. Existing project-local copies seed this cache without another download. It works offline while the cached model is present; clearing the cache can require downloading again. Other CLI platforms use the local application-data directory under `Echo/models`. Classification results are cached separately per photograph inside each project and reused while compatible; changing the photograph, model or classification algorithm, explicitly resetting Depth, or removing its saved classifications requires new classification. Logs distinguish downloading the model, reusing it, computing classifications and reusing saved classifications.

Open **Echo → Dependencies & Attribution…**, **About Echo → Attribution…**, or the attribution button beside the model-dependent settings for the source, privacy, cache and licensing details. The runtime and model repositories declare MIT licenses, but commercial status of the model weights is unresolved because the ADE20K original-image terms restrict dataset use to non-commercial research and education without explicitly settling downstream use of trained weights. See [third-party notices](THIRD_PARTY_NOTICES.md) for the exact dependencies, pinned revision, checksum and source links.

Depth progress uses coloured segments for the steps enabled in the run: classification, stereo depth estimation, optional cliff recovery, and saving the final result. Each segment has equal space and tracks that step's completed items; this is work progress, not a time estimate. Finishing classification only fills its segment. Depth reaches 100% after the final result is saved.

**Texture → Reduce sky colour bleed** adds an optional skyline sampling margin (start at 8 source-photo pixels). It detects smooth sky-coloured regions connected to a photo's top edge and extends nearby foreground colour across a narrow band. It applies to baking, seam correction and inferred boundary colours without editing source photos. This heuristic can miss sky or misidentify pale objects; stronger margins can soften edges. It does not conceal large geometry/projection errors. Changing it rebuilds Texture and Export only.

**Features → Processing acceleration** selects Auto, CPU, or Metal across feature pyramids, descriptor matching, alignment verification and triangulation, depth cleanup, cloud verification, fusion, mesh evidence and extraction, and texture processing. Depth's stereo search keeps its separate backend control. Small jobs and operations outside GPU eligibility continue on CPU. Auto also falls back when Metal is unavailable or a compute allocation exceeds its budget; explicit Metal reports failures for eligible work.

Atlas packing preserves UV islands and searches the gaps between their padded shapes for smaller islands. On macOS, Metal tests positions and quarter-turn rotations in parallel. The processing backend setting applies: CPU disables Metal, Auto falls back to the deterministic CPU search when necessary, and Metal requires acceleration for eligible searches. The packer increases texture density only when the entire layout fits, retaining the previous skyline layout otherwise. Gap search has a deterministic budget of 67,108,864 candidate positions across its eight attempts; exhaustion restores the best complete layout. CPU and Metal spend the same budget. Metal searches ordered bands and uploads only changed occupancy rows. Progress reports the backend and measured density gain. New Texture runs use this packing algorithm; saved atlases remain unchanged.

The processing banner shows the current operation and progress. Toggle **Telemetry** to view the last ten minutes of memory, cache and CPU usage, plus individual thread CPU usage on macOS. Measurements include the processor and registered native workers. CPU capacity uses all logical cores; each thread's percentage uses one core. History is collected while the project is open, and older runs may have memory measurements only.

**Performance** opens a “What happened” node graph from preparation through each stage to the outcome. Nodes show elapsed time, built/reused checkpoints and recorded Metal work; selecting a stage opens its detailed timeline and sortable table. Every processing start or resume records a separate attempt, including cached checkpoint checks, concurrent work, named operations, host transfer/wait time and measured Metal command-buffer execution. Compare any two saved attempts or export the JSON. Timings are inclusive and overlapping rows must not be summed into wall time. GPU batch time is attached to the call that completes the command buffer; it can include previously encoded kernels. Process CPU time excludes external workers. Traces refresh about every five seconds, retain at most 20,000 detailed intervals plus stage boundaries, and keep complete aggregate counts/timings. Runs created before tracing have no historical trace.

Resource settings default to **Automatic**. Depth uses the job’s working-memory allowance after cache and headroom reservations, and adapts concurrent images and worker counts without lowering quality. Manual memory choices use powers of two (MiB/GiB); existing explicit limits remain respected. Old projects may contain a saved manual limit: choose Automatic in Depth settings to remove it. Resuming reads the current project memory settings while keeping the run’s reconstruction inputs frozen.

Sparse alignment publishes sampled points during triangulation and refinement; Fusion publishes sampled integrated voxels during construction and switches to the final surface when complete. Live previews are provisional, bounded and throttled to about three seconds, with two atomic snapshots retained per stage. The viewer preserves its camera while updating. Selecting a sparse or dense camera emphasizes all points geometrically inside its viewing frustum; sparse point selection still shows observation rays.

Starting a new run keeps compatible previous results visible while checkpoints are validated, with a saved-result notice. Validation stays in the preparation banner rather than replaying stage progress bars; reused images do not regenerate live-preview indexes.

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

The backend contract is `ComputeBackend` in C# and Swift; JSON names remain `auto`, `cpu`, and `metal`. Kernel selection uses the `ComputeKernel` enum; native symbol names stay inside the Metal adapter. General compute sessions are serialized per device, release the device between feature blurs so CPU descriptor work can overlap across images, reuse bounded native buffers, and are capped at 512 MiB or one quarter of the run's working-memory allowance, whichever is smaller. Large depth-map sets retain the paged CPU path. Managed staging arrays and semantic projection readback buffers are pooled. Algorithm fingerprints include the backend and each operation’s kernels and shared helpers, so unrelated shader edits preserve checkpoints. Packing has its own implementation fingerprint and integer-exact execution record. Changed GPU arithmetic does not reuse incompatible checkpoints. Metal float arithmetic can change numerical results; verification/refinement and mesh topology decisions retain CPU checks where applicable.

GPU texture baking currently requires sky suppression to be off and atlases no larger than 8192 pixels. Padding accelerates gutters up to 32 pixels; Triangulation and UV warm starts are available with explicit Metal selection and retain double-precision verification/refinement. Auto keeps both on CPU because they did not consistently beat the reference in measured workloads. Automatic batched epipolar scoring and seam diffusion require at least 16,384 matches/nodes. Surface extraction accelerates sparse corner lookup while retaining CPU topology and welding. Bundle adjustment, graph cuts, mesh repair, and simplification remain CPU operations.

Run actual hardware parity checks and optional transfer-inclusive microbenchmarks with:

```bash
dotnet tests/Echo.Tests/bin/Release/net10.0/Echo.Tests.dll --compute --require-metal
dotnet tests/Echo.Tests/bin/Release/net10.0/Echo.Tests.dll --compute --require-metal --benchmark
```

Example Apple M5 Pro microbenchmarks (median of three warmed runs, staging/readback included, other packing/baking work paused):

| Operation | CPU | Metal | Speedup |
| --- | ---: | ---: | ---: |
| 1024 × 1024 descriptor pairs, 128 components | 56.75 ms | 4.77 ms | 11.9× |
| 2048² Gaussian image, sigma 1.6 | 54.13 ms | 5.48 ms | 9.9× |
| 1024² depth cleanup | 18.74 ms | 5.67 ms | 3.3× |
| 125,000-sample surface extraction | 43.61 ms | 22.62 ms | 1.9× |
| 16,384-node seam diffusion, 32 passes | 2.83 ms | 1.19 ms | 2.4× |

The bounded packing search was also checked against the saved 32,010-island cliff scan. At the normal 2K planning resolution, gap-search time fell from 68.57 s to 0.66 s while retaining exactly the same skyline scale and coverage (36.514%). These are individual runs on the same M5 Pro, not a warmed median. At direct 8K resolution the bounded search stopped after 0.46 s and retained the 44.050% baseline coverage; it did **not** reproduce the previous uncapped search’s 14.05% linear-density gain, which took 1,448.48 s. The bound deliberately limits search quality and work; the packer API accepts a larger candidate budget for explicit comparisons.

These are component timings, not an end-to-end reconstruction speedup. The small fusion fixture improved only about 1.1× with Metal integration alone. Performance depends on scene size, GPU, available memory and CPU/GPU transfer costs.

Fusion grid preparation now uses bounded CPU worker batches, deduplicates contiguous cell ranges, and merges sequential binary scratch files. It no longer inserts candidate cells into a shared SQLite database. Ranges use explicit fixed-width little-endian codecs without reflection, and remain compressed until integration consumes a bounded batch. Cell counts and chunk offsets support more than two billion candidates. Fusion version 9 invalidates previous fusion checkpoints; there is no legacy planner. Earlier compatible stages can still be reused.

Benchmark preparation independently against a saved run with Alignment, Depth and Cloud artifacts:

```bash
dotnet tools/Echo.Benchmarks/bin/Release/net10.0/Echo.Benchmarks.dll fusion-planning \
  /path/to/project.echo RUN_ID 5000000 0 /tmp/new-fusion-benchmark \
  > /tmp/fusion-summary.jsonl 2> /tmp/fusion-progress.jsonl
```

The point limit selects the first N saved cloud observations; zero uses the full cloud. Zero workers selects the automatic budget. The output directory must not exist. The project is read-only; scratch stays in the output directory. Progress and summary records are JSONL using source-generated JSON metadata. Timing excludes artifact loading and subsequent integration.

On the saved Cliff scan, five million observations took 75.44 seconds for all preparation phases with 18 workers, producing 2.90 billion candidate cells represented by 694 MiB of final range storage and no SQLite index. This storage figure excludes temporary merge files that were deleted during processing. The old live run had spent about 17 minutes reaching 5.15 million observations during coverage generation alone. The new preparation rate is roughly 13× faster against that observation; this is not a controlled full-fusion comparison, and the complete 244.6-million-observation scan has not been timed with the new planner.

See [AGENTS.md](AGENTS.md) for code conventions, [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md) for licensing.
