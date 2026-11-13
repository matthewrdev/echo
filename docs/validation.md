# Validation

Checked on Apple Silicon macOS on 13 November 2026, using .NET SDK 10.0.300 and Swift 6.3.

## Current checks

- Release solution build: no warnings or errors. C# integration suite: **25 passed**.
- Native SwiftUI/AppKit build and tests: **19 passed**, including saved real artifacts and a Metal-rendered red/blue texture-orientation regression.
- Source formatting/conventions: no violations. Services use MEF and constructor injection; one named type per file.
- UV tests cover perspective projection at varying depth, rotation, non-overlapping triangle interiors and island padding. The synthetic triangle-island packing test achieves **63.4% surface coverage** at 512 × 512 with two-pixel gutters.
- Explicit reset tests prove that resetting Depth rebuilds individual depth maps and all dependent artifacts, retains Alignment, preserves historical resume inputs, and refuses a reset during an active run.
- Native settings round-trip the larger depth width, refinement-sample and stereo-neighbour controls. Dependency warnings start at Depth for these changes.

Run `scripts/check-format.sh`, `dotnet build Echo.slnx -c Release -m:1`, and `dotnet tests/Echo.Tests/bin/Release/net10.0/Echo.Tests.dll`. Native checks use `swift test --package-path macos`; Metal access is needed for rendered-pixel checks. Set `ECHO_TEST_PROJECT` and `ECHO_TEST_RUN` to exercise saved real artifacts.

The [core performance review](performance-review.md) records exact depth-output comparisons, measured allocation reductions, concurrency results and unresolved bottlenecks. Quit-confirmation regressions cover keeping work running, waiting for confirmed shutdown, repeated quit requests and shutdown failures.

## Native quit confirmation

Verified in a separate `Echo Quit Check` bundle against a three-photo fixture inside the repository. Command-Q during processing showed Keep Processing as the default and Stop and Quit as the explicit alternative. Keep Processing returned to the active job. Confirming Stop and Quit on a longer run recorded `runCancelled` for `66955d989e634f63aa1e43e9fcea5eda`; both the test app and its CLI subprocess exited. Another run finished while the prompt was open and exited without losing its completed artifacts. The user's active Cliff depth run was not interrupted.

## Texture correction and packing

The native viewer incorrectly converted saved UV `v` to `1-v`. This mapped triangles to unrelated atlas islands. A rendered regression reproduces the incorrect red/blue row swap and now passes with direct UV coordinates. The actual GLB was rendered from a recovered camera and compared visually with the corresponding source photograph: recognizable rock layers now map to the appropriate surface regions. The large white gaps are missing geometry, not fixed by this texture correction.

A texture/export-only rebake used the existing 48-photo geometry in `/private/tmp/echo-texture-review.echo`, run `e3e504c6f9974b8191e97073d5215587`. It is **not a new full reconstruction**. The 8192 × 8192 atlas contains 72,886 charts with **33.9% surface-texel coverage**, up from 28.6% before packing changes; these percentages exclude gutters. Fragmentation still makes packing inefficient. No seam colour balancing is implemented.

The rebuilt GLB has 660,681 triangles and passes Khronos glTF Validator with **0 errors and 0 warnings**. Structural validity does not establish reconstruction quality, geometric accuracy or usable surface coverage.

## Saved real-photo result

The small test pack contains 48 DJI FC8482 photographs, each 4032 × 2268 pixels, from `3d-assets/processed/the-quiet-place`. The local project is `/private/tmp/echo-quiet-place-v1`, with run `b00e645ca35547eeae4bcfa47a134e4d`.

| Measurement | Result |
| --- | ---: |
| Registered cameras | 48 |
| Refined global tracks | 992 |
| Track observations | 4,157 |
| Median retained reprojection error | 0.465 pixels |
| Valid depth samples | 4,800,564 |
| Multiview-supported dense samples | 4,457,016 |
| Exported triangles | 660,681 |
| Embedded texture | 8192 × 8192 JPEG |
| Khronos glTF Validator | 0 errors, 0 warnings |

The native dense viewer displays the bounded sampled preview and labels the displayed count. The complete dense cloud is retained separately in `cloud.ply`.

The corrected GLB was rendered and visually inspected. It still has substantial holes, fragments and unreliable vegetation/recesses; it is not an acceptable final reconstruction. **The requested approximately 1.5-million-triangle result has not been reached.** Fusion currently coarsens its voxel grid too aggressively when estimating the face budget. Texture and depth-setting fingerprints changed intentionally. Larger resolutions and more search samples are available, but a higher-quality full field reconstruction has not yet been validated. Line segments and junctions are now detected and exposed as matching candidates, but standalone line constraints and a robust scale/rotation-invariant point descriptor are still missing.

The reconstruction has not been checked against survey control. Reprojection error is not an absolute position/shape accuracy measurement. The same-cliff Red-Point GLB was unavailable locally; a different cached Red-Point cliff model was inspected only as a structural reference. No geometric accuracy comparison is claimed.

A 500+ photo reconstruction and mixed drone/mirrorless field dataset have not been validated. Native UI coverage is incomplete, and the bundled app uses a local ad-hoc signature rather than notarisation.
