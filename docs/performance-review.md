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

## Feature detection follow-up

The first three photos in the 193-photo Cliff project were extracted directly, without reusing feature checkpoints. Each case ran five times; figures below are medians of iterations 1–4, after the initial JIT/pool warm-up. Image decoding is included; JSON serialization and output hashing are excluded. An independent user depth run overlapped later measurements, so elapsed times are indicative rather than an isolated CPU benchmark. Allocations are cumulative managed bytes, not peak RSS; pooled buffers retain memory for reuse.

| Three-photo case | Before | After | Managed allocations before → after |
| --- | ---: | ---: | ---: |
| 1000 px, 800 points/photo, lines enabled | 1.014 s | 0.495 s | 210.6 MB → 34.6 MB |
| 4032 px, 20,000 points/photo, lines enabled | 7.680 s | 5.134 s | 1726.7 MB → 528.9 MB |
| 4032 px, 20,000 points/photo, points only | 4.512 s | 0.936 s | 1080.1 MB → 359.8 MB |

All three complete output files were byte-identical to the previous implementation: coordinates, point/line ordering, kinds and every descriptor value. Default output contains 2,400 points and 110 lines; full-resolution output contains 60,000 points and 1,434 lines. These are extraction benchmarks, not evidence that the reconstructed model is correct.

Changes: borrow native BGRA/RGBA pixels through spans when their layout and alpha semantics permit; area-downsample directly into grayscale; compute Harris products once in five rolling rows; sort corners in place with the original tie order; reuse a stack descriptor; pool line scratch storage and retain three smoothed rows; retain only the strongest requested lines with stable ties; cache line directions and remove per-junction LINQ closures. Other pixel formats, padded rows and premultiplied alpha retain Skia's conversion path. No unsafe pointer code was necessary.

Regression checks include original-implementation hashes for checkerboard, noise and rectangle inputs; concurrent extractions alternating image dimensions; cancellation; and channel/alpha/stride parity. Detector fingerprints remain unchanged because results are preserved.

```sh
dotnet tools/Echo.Benchmarks/bin/Release/net10.0/Echo.Benchmarks.dll features PROJECT 1000 800 5 local-results/features.json 3 true
```

Local comparison outputs and timing logs are under `local-results/feature-optimization/` (ignored).

## Dense-cloud batching follow-up

All 48 saved Quiet Place depth maps were loaded before timing. Three builds per configuration; medians of the last two warm builds. Colour-image decoding is included; PLY writing/hashing is excluded. A separate user processing job overlapped these measurements, so timings are indicative. Managed allocations are cumulative, not peak resident memory.

| Configuration | Elapsed | Managed allocations |
| --- | ---: | ---: |
| Previous serial builder | 17.396 s | 2385.2 MB |
| Batched builder, one worker | 3.651 s | 419.9 MB |
| Batched builder, four workers | 3.215 s | 416.5 MB |
| Batched builder, automatic workers | 2.975 s | 415.6 MB |

Every configuration retained exactly 4,457,016 of 4,800,564 input samples, in the same order, with byte-identical PLY output (`0b20994fe72231caa7b0c7cf95b68882b08c08a8e1b249d52247b2dcfad0465b`). These are performance/parity checks, not proof of reconstruction quality.

The builder indexes photos/cameras once, keeps a bounded nearest-neighbour selection, reads colour pixels without a full-resolution managed copy, and processes row batches into pooled private buffers. Only the caller merges results and publishes progress/previews. Worker scratch is capped at 64 MiB including pool rounding; depth maps and the final cloud are additional memory. CPU worker count and batch rows are execution settings, exposed in the native app without invalidating compatible artifacts. Synthetic tests cover deterministic serial/parallel output, cancellation, callback ownership and buffer reuse.

```sh
dotnet tools/Echo.Benchmarks/bin/Release/net10.0/Echo.Benchmarks.dll cloud PROJECT RUN 3 local-results/cloud.ply 4 8
```

## Remaining core kernels: independent parity checks

The `core` benchmark runs the same fixture with the previous app's assemblies and the updated assemblies in separate processes. It covers 800 × 800 descriptor matching; 180 relative-pose observations with outliers; two GPS cameras plus a visually registered ground camera, rotation refinement and bundle adjustment; a curved 2,400-triangle grid simplified to 800 triangles; and texture baking plus GLB export. Three iterations per process; medians below exclude the initial warm-up. These small fixtures include output hashing/serialization (and texture/export I/O), so they establish output parity and allocation direction, not large-scene throughput. Allocation figures are cumulative managed bytes, not retained or peak memory.

| Fixture | Before → after elapsed | Before → after allocations |
| --- | ---: | ---: |
| Descriptor matching | 18.9 → 13.5 ms | 0.209 → 0.171 MB |
| Relative pose | 17.5 → 13.0 ms | 31.563 → 12.150 MB |
| Mixed-camera alignment and refinement | 20.8 → 17.0 ms | 29.242 → 12.936 MB |
| Mesh simplification | 146.5 → 35.7 ms | 114.066 → 3.393 MB |
| Texture baking and GLB export | 77.3 → 58.1 ms | 6.235 → 3.382 MB |

All five serialized output hashes are identical before/after; the exported GLB is byte-identical. The eight-camera real depth benchmark also retains its complete reference hash (`f0850c381ce6363df840b34174d47d83484584d57183815f2aed3629067d24ba`, 745,017 valid samples) after pooling worker hypothesis/cost buffers and median-filter scratch. Its latest warm median was 2.295 s and 2,363 MB allocated; full image decoding still dominates allocations.

```sh
# Use a fresh disposable output directory; this creates its own fixture project.
dotnet tools/Echo.Benchmarks/bin/Release/net10.0/Echo.Benchmarks.dll core local-results/core-fixture
```

Local logs: `local-results/core-before.log`, `core-after.log` and `core-depth-after.log`. This is not a claim that every allocation or every LINQ call has been removed. Stable setup sorting and artifact materialization remain where they do not justify a risk to numerical ordering. No unsafe code was needed for these changes.

## Cliff cloud crash and consistency

The 193-photo Cliff cloud run was replayed from its saved alignment and depth maps, without editing the user's project. The crash reproduced in `DepthView.Difference`: a near-camera-plane projection exceeded `Int32.MaxValue`; converting it before bounds checking let `ix + 1` overflow. Bounds are now tested in floating point before conversion, with finite positive depth and map dimension checks. A regression test includes tiny positive Z, extreme coordinates, NaN, infinity and points behind the camera. Run failures now retain the exception stack in the NDJSON log as well as the short UI message.

With the bounds fix, all 193 views completed: 42,352,518 of 50,105,923 depth samples retained in 30.019 s (Build timing; excludes loading depth maps and writing PLY). Stronger consistency then retained 38,899,757 in 30.591 s: every contributing bilinear corner must agree in depth, the sampled surface must project back within 1.5 depth-map pixels, and viewing rays must differ by at least 0.5 degrees. Both tolerances are configurable. The existing minimum support of one neighbour is preserved; two or more is a useful stricter option when coverage permits.

Before/after XZ, XY and ZY projections were inspected using the `compare-cloud` benchmark command. Rejection of 3,452,761 extra samples **does not establish that the floating shell is fixed**. Separated/fragmented surfaces remain visible, and no ground-truth surface comparison or complete corrected mesh has passed acceptance. These checks cannot fix biased camera poses or mutually consistent wrong depth.

## Allocation and concurrency findings

| Routine | Changes made | Remaining concerns |
| --- | --- | --- |
| Image preparation and depth pyramid | Decode each reference/neighbour once per job. Area-downsample directly into grayscale through spans. Preserve channel rounding. | Decoded full-resolution arrays are still sizeable and duplicated between concurrent jobs. A shared bounded image cache could help. |
| Depth search | Reuse row-local patch, hypothesis and cost spans; reuse median-filter storage. Bound concurrent images and divide the worker budget between them. Persist per-image completion and report pyramid progress. | CPU patch correlation remains expensive at high resolutions. The memory setting is a scheduling estimate, not a hard process limit. Widths above the source size can repeat native-resolution refinement. Missing sparse support can still leave holes. |
| Feature extraction | Native pixel spans, rolling gradient-product rows, pooled line scratch, stable in-place corner sorting and reusable descriptors. See the measured follow-up above. | Full candidate sorting remains; line seed sorting and region traversal dominate the large-image line case. Point descriptors are intensity patches, not scale/rotation-invariant descriptors. |
| Descriptor matching | Pass descriptor-bearing features directly through spans, pool nearest-neighbour scratch, and retain the original scalar accumulation and tie order; separate point and reversible line descriptors. | Exhaustive comparisons remain quadratic in feature counts. No approximate index or GPU matcher yet. |
| Camera projection and registration | Value-type projection, bounded stable random-key sampling, reusable inlier lists and epipolar vectors, direct normal-equation accumulation and indexed camera lookup. Alignment copies only the required camera priors instead of serializing whole photos/EXIF. | SVD still uses MathNet allocations. Registration observation keys remain rounded strings; changing their identity needs a separate artifact compatibility review. |
| Bundle adjustment | Reuse observation evaluation, Jacobian, index and Schur-product buffers. Save camera states through array references (trial steps replace arrays), use value-type projection and stack step vectors, and avoid per-iteration JSON camera cloning. | Camera Hessian and solve remain dense. Dense camera algebra and stored point cross-blocks become the principal scaling limit. Sparse/block solving is a larger algorithm change. |
| Dense cloud | Indexed camera/photo lookup, bounded neighbour selection, pooled parallel row batches, deterministic merges and direct colour resizing. Reserve exact capacity when loading validated binary PLY. | All depth maps and accepted samples remain resident. Growing the construction list and generating previews still allocate. |
| Fusion | Fill one footprint array, sort in place, index source cameras/photos, copy candidate keys directly, and preallocate the retained voxel list. | Voxel candidate expansion and candidate-by-view traversal can dominate time and memory. Spatial indexing and chunked volume processing are still needed for large scenes. |
| Surface extraction | Reuse corner values, quad indices and signs; cache source-camera projection. | Geometry is stored as jagged vertex/face arrays. This retains many small objects even after temporary allocations are removed. |
| Edge collapse | Reuse neighbour/intersection/affected-face sets and buffers; count incident edges in one pass for boundary checks. Preserve candidate order, arithmetic and topology checks. | Boundary scans and stale priority-queue entries remain. A revision-aware adjacency cache needs separate topology validation. |
| Texture baking and packing | Reuse projected triangle/UV scratch and silhouette triangle storage. Cache camera transforms, use value-type texture coordinates, mutate owned chart buffers, and combine repeated bounds scans. | Chart construction and packing are still serial. Fragmented geometry creates many tiny islands. No seam/exposure balancing. |
| JSON and GLB | Stream JSON reads, writes and hashes. Stream GLB directly into an atomic output file instead of retaining duplicate full-size buffers. Hash/byte compatibility and failure atomicity are tested. | The JSON artifact schema still has large arrays, and progress events persist the full run snapshot. Binary numerical artifacts and throttled progress persistence are future changes. |

These memory changes preserve numerical operations and saved artifact contracts; their algorithm fingerprints remain unchanged. Adding line/junction detection changes feature and matching fingerprints so old point-only results are not reused as new line results.

## Lines and preview validation

A separate three-photo Quiet Place fixture under `local-results/line-preview.echo` produced 45, 45 and 43 line segments. Its three image pairs contained 16, 19 and 16 candidate line matches. No junctions passed the detector on these three particular images; synthetic corner/rectangle tests exercise junction detection and its point-match path.

The native feature viewer draws selectable segments and junction markers. Matching includes selectable segment correspondences and metadata. Old saved feature artifacts show an explicit notice that they need to be regenerated to contain line data. Standalone line matches are inspection candidates, not geometrically verified 3D-line constraints. Junction point observations participate in the existing point alignment. Generic semantic shapes are not detected.

The sensitivity/region-splitting follow-up found 134 line candidates across the first three Cliff photos at 1000-pixel analysis width, versus 110 previously. Inspected overlays show additional rock-edge segments and some sky/cloud edges. This is a modest detector improvement, not evidence of verified 3D line reconstruction. Synthetic tests exercise faint edges and curved regions with recoverable straight subsegments; legacy settings retain the original feature hashes. New threshold and splitting settings are exposed in the native editor and invalidate Features onward.

## Correctness limits

The previously inspected real model still contains holes and fragments; it has not become a correct final model because the code runs faster. The approximately 1.5-million-triangle target and survey-accurate scale remain unvalidated. Geometry quality, robust descriptors, missing-depth support, sparse bundle solving and a real line-constrained reconstruction should be addressed before claiming production quality.

A future Metal depth implementation should operate on batches of resident image pyramids, with the current CPU path retained for numerical comparison. No GPU processing backend is implemented in this change.
