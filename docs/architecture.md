# Architecture and persistence

The process boundary is the CLI. The desktop can be replaced without changing project state or reconstruction behaviour. The project format is a directory with schema-versioned JSON and content-addressed artifacts; a project can be copied to another machine without its original photo directory.

```text
Native macOS shell (SwiftUI / AppKit / SceneKit)
    → CLI subprocess (JSON / NDJSON)
        → MEF command handlers
            → Processor → ordered IPipelineStage exports
                → injected subsystem interfaces
```

`Echo.Composition` owns MEF catalogs and container disposal. Services export their interfaces with shared lifetimes and use importing constructors. Stateful project stores and run contexts are created by injected factories; they are never global singletons. `Processor` coordinates run lifetime, source validation and stage order. `CheckpointStore` owns atomic artifact creation and hash validation. Each stage reads persisted prerequisites and publishes its own output.

C# contracts contain one named type per file. Serialization and file operations live in `Echo.Infrastructure`. The Swift shell uses constructor-injected protocols for CLI transport, project commands and artifact loading. Its composition root is `EchoApplication`; it has no dependency on C# implementation assemblies.

## Project layout

```text
project.json                        # current catalogue, hints and settings
revisions/000001.json                # saved catalogue revisions
sources/<id>.original.jpg            # untouched input bytes (extension retained)
sources/<id>.jpg                     # orientation-normalised working JPEG
previews/<id>.jpg                    # small viewer image
runs/<run-id>/run.json               # frozen project snapshot, target and artifact links
runs/<run-id>/events.ndjson          # append-only progress/event stream
runs/<run-id>/cancel                 # cooperative cancellation request
artifacts/<stage>/<fingerprint>/
    manifest.json                   # algorithm/schema versions + SHA-256 for every output
    ... stage files ...
work/<temporary-id>/                 # uncommitted stage output
```

An image ID is derived from the SHA-256 of the original bytes. Working-image hashes are verified before processing. Raw metadata stays in the catalogue; hints overlay it at run time. Import checkpoints after each photograph and deduplicates subsequent imports. Import, edit and run operations have separate exclusive locks; edits can proceed while a run uses its frozen snapshot.

A stage writes to a unique work directory, writes its output hashes and then atomically renames the directory into the artifact store. Only committed directories are referenced by a run. Run/project JSON is replaced atomically. Reuse verifies output hashes; corruption is an error, not a silent cache hit. An abandoned work directory after a hard crash can be removed once no run owns the project. Resume retains completed per-image/per-pair artifacts and retries any unfinished unit.

## Stage outputs

| Target | Persisted output |
| --- | --- |
| import | Catalogue revision, original, normalised image, preview and raw metadata per photograph |
| features | Full-resolution feature coordinates and descriptors per image; aggregate `units.json` |
| matching | Correspondences, optional automatic acceptance/rejection diagnostics and manual ties per pair; aggregate `units.json` |
| alignment | `scene.json`, `points.ply`; interim inspectable `alignment-preview` scene artifacts |
| depth | Depth/confidence arrays per image, dimensions and image ID; aggregate `units.json` |
| cloud | `cloud.ply` (binary coloured points with source-view index and support), `cloud.json` report and bounded preview; interim immutable `cloud-preview` artifacts |
| fusion | `volume.json`, sampled `volume.preview.json`, fusion report |
| mesh | `mesh.json` and `mesh.ply`, with source-camera IDs for every triangle |
| texture | `atlas.jpg`, `texture.json` (UVs, chart count, covered texel fraction) and `mesh.json` with seam vertices |
| export | `cliff.glb`, `cliff.geo.json`, `cliff.report.json` |

Zero depth means invalid/unobserved. Depth is camera-forward Z distance. Confidence is a photometric score, not metric uncertainty. The covered texel fraction excludes gutters and must not be interpreted as reconstructed cliff coverage. Sparse cloud entries retain pair observation references to refined global tracks. Dense points are coloured multiview-supported depth observations; overlapping measurements remain in the cloud until volume fusion.

## Reconstruction details

Candidate pairs use capture order, nearby GPS locations and explicit overlap/tie hints. Harris corners have normalised 8×8 intensity descriptors; mutual nearest-neighbour and ratio tests reject weak matches. This provides a small from-scratch baseline, with substantial sensitivity to changing viewpoint and scale.

GPS plus DJI gimbal angles initialise camera-to-world poses. Positions are WGS84 ECEF differences rotated into local East/Up/South. Camera coordinates are X right, Y down, Z forward. Rotations are row-major camera-to-world matrices. Initial robust epipolar updates refine rotations. A Schur-complement bundle adjustment then jointly refines camera positions, rotations, shared focal length and multi-image tracks, with robust reprojection loss and loose GPS priors. Triangulation rejects negative depth, very low parallax and excessive reprojection error.

Without two georeferenced cameras, a calibrated eight-point essential-matrix RANSAC chooses a visual seed with positive-depth checks. A calibrated DLT/PnP RANSAC registers additional cameras against existing 2D–3D correspondences. A visual-only seed has arbitrary scale. Ground-camera input should therefore be treated as an experiment rather than a supported quality guarantee.

Stereo starts at 160 pixels and refines through an image pyramid to the requested resolution. It uses three nearby cameras, warped patch correlation, inverse-depth hypotheses and sub-plane refinement. Sparse depths narrow the search range. Cross-view checks produce a coloured dense cloud before surface generation. Projective TSDF fusion integrates only observed near-surface voxels with at least two supporting views. Surface nets connect shared cell vertices. Quadric edge collapse reduces excess triangles while checking topology, normals and open boundaries. No triangle sampling is used for exported geometry.

Faces select a depth-supported source photograph. Texturing welds geometry for adjacency, grows connected charts under a normal-angle constraint, and rejects overlapping projected triangles. Charts are flattened onto local tangent planes and packed as variable-size rectangles with a shared texel density. Seam vertices are duplicated into a separate texture mesh. Each covered texel is projected back into its face’s source photograph with perspective-correct interpolation and bilinear sampling; a configurable gutter is dilated around chart boundaries. The export uses this texture mesh, while older texture artifacts without `meshFile` still use their original mesh. GLB positions are local floats to avoid precision loss from large world coordinates. The sidecar's 4×4 row-major matrix transforms a column-vector `[east, up, south, 1]` into approximate WGS84 ECEF coordinates. Unknown altitude datum remains explicit; this is not a survey transformation.

## Commands and errors

`run start --target STAGE` processes dependencies through that target. `run resume ID` repeats the saved target with saved inputs. Starting a new run applies edits while reusing unaffected artifacts. `pipeline plan` lists required stages and image count; it does not promise cache hits before input/output validation.

| Exit | Meaning |
| --- | --- |
| 0 | success |
| 2 | invalid arguments, JSON or settings |
| 3 | project/input/checkpoint problem |
| 4 | unexpected processing or IO failure |
| 5 | reconstruction needs better inputs/hints |
| 6 | another process holds the operation lock |
| 130 | cooperative cancellation |

Events include schema version, increasing sequence, timestamp, run ID, type and payload. Their full stream is retained on disk and can be replayed with `run events ID`. The desktop consumes the same interface as scripts. Closing the desktop does not explicitly kill a CLI job; use the persisted cancellation command to stop one. A hard-killed process may leave run status `running`; a new process can resume once the OS releases its lock.

## Next engineering steps

Next priorities are scale/rotation-invariant features, lens-distortion calibration, improved depth normals and occlusion reasoning, better chart parameterisation and cross-camera seam blending. Keep those as replaceable subsystem implementations while preserving the versioned project/artifact contracts. A full 500+ image benchmark should precede claims about scale or reconstruction quality.

## Settings and invalidation

Each stage fingerprints its own work controls and prerequisite artifact identities. Feature edits propagate through matching; depth edits propagate through cloud; cloud validation edits propagate through fusion; texture edits preserve geometry. `faceLimit` affects fusion as well as simplification. Matching, cloud, fusion and texture implementations have explicit version inputs, so their changed algorithms cannot reuse the earlier baseline outputs. Saved run snapshots remain immutable. The UI compares current settings, enabled photos and hints against the viewed snapshot to label dependent artifacts out of date, and explains the impact before edits.

The dense preview is sampled and viewable while points are accepted. It is not a resumable partial cloud computation; an interrupted cloud stage reruns from saved depth maps. The map view keeps its camera position across state refreshes, uses native dragging, and scopes mouse-wheel zoom to the visible map. Tilt colour is computed from the camera's forward rotation axis, so it also works for aligned poses and hints.

## Explicit stage reset

`pipeline reset --from STAGE` takes the project run lock and assigns new stage revisions from that stage through export. `RunContext` includes the owning stage revision in each checkpoint fingerprint, including individual feature images, match pairs, and depth images. With no explicit reset, legacy fingerprint inputs are preserved. Saved run snapshots retain their original revisions, so resuming a historical run retains its original cache. No artifact files are deleted. Native invalidation compares these revisions as well as settings, images and hints.

Texture baking uses minimum-area chart orientations, rotated skyline packing and conservative silhouette packing with protected gutters. The layout with the greater scale wins. Gutters dilate globally from painted pixels so overlapping island bounding boxes do not repeatedly spread padding. Atlas and GLB UVs have a top-left image origin; SceneKit receives them without a vertical flip. A rendered red/blue test covers this convention.
