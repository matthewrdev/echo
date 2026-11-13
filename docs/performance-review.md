# Core processing review — 13 November 2026

This review covers image preparation, features, matching, reconstruction, bundle adjustment, depth, dense validation, fusion, meshing, simplification, texturing, export and checkpoint I/O. It separates allocation cleanup from unresolved algorithmic and reconstruction-quality limits. Processing is C# on the CPU; Metal currently renders the native viewer only.

## Measured depth changes

Read-only input: the 48-photo Quiet Place project, saved run `b00e645ca35547eeae4bcfa47a134e4d`. The benchmark uses its existing alignment, 1024-pixel depth width and original depth settings. Three iterations per configuration; median elapsed time. Managed allocation counts are cumulative bytes allocated during estimation, **not peak resident memory**. Timing excludes output serialization and checkpoint hashing. Source decoding is included; OS file caching can affect these short runs.

| Comparison | Before | After | Output check |
| --- | ---: | ---: | --- |
| First camera, original vs reduced allocation estimator | 4.281 s | 0.620 s | Identical complete depth/confidence hash; 69,285 valid samples |
| First camera, managed allocation | 926.5 MB | 283.3 MB | Same numerical output |
| Eight cameras, one image × 16 workers vs two images × 8 workers | 4.054 s | 2.650 s | Identical ordered output hash; 745,017 valid samples |

The eight-camera configurations both allocated about 2.394 GB in total. Concurrency improves elapsed time here; it does not remove the remaining allocations. This is not a full reconstruction benchmark, nor evidence of 500-photo scalability or geometric accuracy. The earlier single-camera and later batch tests use different worker allocations and should not be combined into one overall speedup.

Reproduce the batch comparison after a Release build:

```sh
dotnet tools/Echo.Benchmarks/bin/Release/net10.0/Echo.Benchmarks.dll PROJECT RUN 1024 3 local-results/sequential.json 8 1
dotnet tools/Echo.Benchmarks/bin/Release/net10.0/Echo.Benchmarks.dll PROJECT RUN 1024 3 local-results/concurrent.json 8 2
```

Local measurement logs and maps are ignored under `local-results/depth-performance/`. Single-camera output SHA-256: `12314452d1115419d86c9f90be6c5e7abf6c375baba2f04e539a6301b93b86d7`. Eight-camera output SHA-256: `f0850c381ce6363df840b34174d47d83484584d57183815f2aed3629067d24ba`.

## Allocation and concurrency findings

| Routine | Changes made | Remaining concerns |
| --- | --- | --- |
| Image preparation and depth pyramid | Decode each reference/neighbour once per job. Area-downsample directly into grayscale through spans. Preserve channel rounding. | Decoded full-resolution arrays are still sizeable and duplicated between concurrent jobs. A shared bounded image cache could help. |
| Depth search | Reuse row-local patch, hypothesis and cost spans; reuse median-filter storage. Bound concurrent images and divide the worker budget between them. Persist per-image completion and report pyramid progress. | CPU patch correlation remains expensive at high resolutions. The memory setting is a scheduling estimate, not a hard process limit. Widths above the source size can repeat native-resolution refinement. Missing sparse support can still leave holes. |
| Feature extraction | Store corner candidates as value types rather than separate heap objects. Line region growth checks cancellation during large regions. | Full candidate sorting and several full-image gradient arrays remain. Point descriptors are intensity patches, not scale/rotation-invariant descriptors. |
| Descriptor matching | Span-based distance calculation without per-comparison arrays; separate point and reversible line descriptors. | Exhaustive comparisons remain quadratic in feature counts. No approximate index or GPU matcher yet. |
| Camera projection and registration | Remove the transpose allocation in general projection; cache camera projection for repeated registration, mesh and texture samples. Reuse DLT scratch storage. | RANSAC samples still sort the observation collection. Essential-matrix scoring still allocates small MathNet vectors per match; both deserve a dedicated numerical regression before replacement. |
| Bundle adjustment | Reuse observation evaluation, Jacobian, index and Schur-product buffers. Eliminate per-observation LINQ concatenation and array copies. | Camera Hessian and solve remain dense. Dense camera algebra and stored point cross-blocks become the principal scaling limit. Sparse/block solving is a larger algorithm change. |
| Dense cloud | Existing per-sample structure is a value type. Reserve exact capacity when loading validated binary PLY. | All depth maps and accepted samples remain resident. Growing the construction list and generating previews still allocate. |
| Fusion | Fill one footprint array, sort in place, and sort candidate keys in place. | Voxel candidate expansion and candidate-by-view traversal can dominate time and memory. Spatial indexing and chunked volume processing are still needed for large scenes. |
| Surface extraction | Reuse corner values, quad indices and signs; cache source-camera projection. | Geometry is stored as jagged vertex/face arrays. This retains many small objects even after temporary allocations are removed. |
| Edge collapse | Reuse plane, candidate-position and affected-triangle buffers. Preserve candidate order, arithmetic and topology checks. | Repeated neighbour sets, boundary scans and stale priority-queue entries remain expensive. A revision-aware adjacency cache needs separate topology validation. |
| Texture baking and packing | Reuse projected triangle/UV scratch and silhouette triangle storage. Cache camera transforms; use value-type texture coordinates. | Chart construction and packing are still serial. Fragmented geometry creates many tiny islands. No seam/exposure balancing. |
| JSON and GLB | Stream JSON reads, writes and hashes. Stream GLB directly into an atomic output file instead of retaining duplicate full-size buffers. Hash/byte compatibility and failure atomicity are tested. | The JSON artifact schema still has large arrays, and progress events persist the full run snapshot. Binary numerical artifacts and throttled progress persistence are future changes. |

These memory changes preserve numerical operations and saved artifact contracts; their algorithm fingerprints remain unchanged. Adding line/junction detection changes feature and matching fingerprints so old point-only results are not reused as new line results.

## Lines and preview validation

A separate three-photo Quiet Place fixture under `local-results/line-preview.echo` produced 45, 45 and 43 line segments. Its three image pairs contained 16, 19 and 16 candidate line matches. No junctions passed the detector on these three particular images; synthetic corner/rectangle tests exercise junction detection and its point-match path.

The native feature viewer draws selectable segments and junction markers. Matching includes selectable segment correspondences and metadata. Old saved feature artifacts show an explicit notice that they need to be regenerated to contain line data. Standalone line matches are inspection candidates, not geometrically verified 3D-line constraints. Junction point observations participate in the existing point alignment. Generic semantic shapes are not detected.

## Correctness limits

The previously inspected real model still contains holes and fragments; it has not become a correct final model because the code runs faster. The approximately 1.5-million-triangle target and survey-accurate scale remain unvalidated. Geometry quality, robust descriptors, missing-depth support, sparse bundle solving and a real line-constrained reconstruction should be addressed before claiming production quality.

A future Metal depth implementation should operate on batches of resident image pyramids, with the current CPU path retained for numerical comparison. No GPU processing backend is implemented in this change.
