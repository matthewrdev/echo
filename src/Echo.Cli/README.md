# Echo CLI

Use the executable path because shells usually provide a built-in `echo`.

```bash
ECHO_CLI=/Applications/Echo.app/Contents/MacOS/echo
# From a source checkout, use ECHO_CLI=./scripts/echo
"$ECHO_CLI" --help
"$ECHO_CLI" images import --help
"$ECHO_CLI" runtime-info
```

Project commands accept both `--project PATH images list` and `project PATH images list`. Give each flag and value as a separate argument when calling from Python or Node. Keep stdout and stderr separate. For concurrent jobs, use separate projects. Inside a project, pass the run ID to queries and exports so a newer run cannot change the selection.

## Output and run control

- Data commands write one JSON value to stdout. Help writes text. With default settings, import and run progress goes to stderr. Use `--events none` to suppress live progress. For a silent start or resume, add `--json` if you want the final JSON result.
- With `--events ndjson`, import, start, and resume write one JSON object per stdout line. Dispatch on `type` and allow unknown fields and event types. A successful import ends with `imageImportCompleted` and an `imported` array. Run records include `runId` and `sequence`. A run that starts ends with `runCompleted`, `runCancelled`, or `runFailed` when the process shuts down normally.
- Check the process exit code. `0` indicates success, `2` invalid arguments or input, `3` an operation error, `4` an unexpected error, `5` a missing requirement, `6` a busy project, and `130` cancellation. The CLI writes a JSON error with `schemaVersion`, `error`, and `exitCode` to stderr. Other diagnostics can precede the error.
- Start and resume are foreground commands. Use a separate invocation of `run cancel RUN_ID` to request a checkpoint-aware stop, and `run status RUN_ID` to read the resulting state. Read retained events with `run events RUN_ID` as NDJSON.

## Reproducible runs

```bash
"$ECHO_CLI" --project "$project" config show > settings.json
"$ECHO_CLI" --project "$project" run start --target export --settings settings.json --headless --events ndjson
"$ECHO_CLI" --project "$project" export-gis --run "$run_id" --output "$output"
```

Edit the exported settings file to set values for an experiment. `--settings` supplies a complete run configuration and leaves the saved project settings alone. Explicit subject-distance and texture flags apply on top of the file. The run saves the effective configuration in its snapshot, which resume reuses.

On macOS, `export-gis` includes a GeoTIFF projection and PNG sidecars when a saved geographic mesh can be rendered. Read the export manifest for the produced files and any omissions. Use `geo-probe --lat DEGREES --lon DEGREES` on a saved geographic mesh to query its topmost surface. Pass `--alt METRES` to place a point in the run's altitude reference instead.

Use `drone scan --source CARD_ROOT` on macOS to get proposed site groups and flight sets from a mounted or copied card. Each group's `photos` array is an `images import --manifest` input. Photos without GPS appear in the `Unassigned` group.


### Drone mapping

Run through Texture or Export, then use `export-map --crs EPSG:CODE --resolution METRES --output NEW_DIRECTORY` on macOS. The export uses local PDAL and GDAL, configurable through `ECHO_PDAL` and `ECHO_GDAL_BIN` (the GDAL executable directory). The orthophoto, DSM, and DTM share a projected grid. Elevation cells without support use no-data. The orthophoto derives from the textured mesh and its source render is capped at 4096 pixels per side. The classified LAZ and elevation rasters use WGS84 ellipsoidal heights when known. Pass `--relative-heights` to explicitly use heights relative to the scene origin.

Import surveyed targets with `hints import FILE`. Each JSON hint has a distinct `id`, a `type` of `ground-control` or `survey-check`, a `position` with WGS84 latitude, longitude, ellipsoidal altitude, `altitudeDatum` set to `WGS84 ellipsoid`, and `uncertaintyMetres`. Specify two registered image IDs with `imageId` and `otherImageId`, and their normalized full-resolution observations as two-element arrays named `pixel` and `otherPixel`. Three or more consistent, non-collinear controls fit a geographic similarity transform and constrain the final bundle adjustment. Check points are held out of both. The alignment artifact and map package include `survey-control.json` with per-point residuals and horizontal and vertical check-point RMSE when measurable.

For RTK or PPK camera positions, import `camera` hints with `position.uncertaintyMetres` for horizontal accuracy and `verticalUncertaintyMetres` for vertical accuracy, or set `--uncertainty` and `--vertical-uncertainty` on `hints camera`. Provide WGS84 ellipsoidal heights to use both accuracy terms during geographic bundle adjustment.

Use `merge-maps MAP_DIRECTORY MAP_DIRECTORY --output NEW_DIRECTORY --max-seam-error METRES` to combine separately reconstructed overlapping parts of a survey. Export all parts to the same projected CRS, pixel grid, and WGS84 ellipsoidal height reference. The merge publishes the three raster products and classified LAZ after checking elevation disagreement in connected DSM overlaps. The mosaic manifest records the seam errors. Later input tiles take priority where data overlap.

### Stitch textured walls into terrain

In the macOS app choose **File → Build Terrain from Input…**, or run the command below. Use a saved terrain input with GDAL-readable elevation and RGB imagery covering the same area. Add geographically aligned, textured ECHO walls by their exact saved run IDs. Each wall needs one connected crop with one open rim. Give adjacent crops room for their terrain transitions.

```bash
"$ECHO_CLI" stitch-terrain terrain.json --output new-guide
```

```json
{
  "elevation": "elevation.tif",
  "imagery": "ortho.tif",
  "verticalDatum": "NAVD88",
  "footprint": "area.geojson",
  "photoOverlays": ["registered-faces.tif"],
  "gridLongEdge": 512,
  "terrainTextureSize": 8192,
  "maxSeamErrorMetres": 5,
  "walls": [
    { "project": "wall.echo", "run": "SAVED_RUN_ID", "heightOffsetMetres": 22.1 }
  ]
}
```

Paths are relative to the manifest. You can omit `walls` until textured reconstructions are ready. An optional WGS84 GeoJSON polygon selects terrain by grid cell centre. Optional `photoOverlays` are georeferenced RGBA rasters blended over the aerial image in list order. Fit ground photographs to the terrain before supplying them as overlays. Use `attribution` to include source credits in the exported GLB. A height offset is added to the saved wall's elevation before fitting and must be supplied if the wall and elevation raster have different or unknown height references. Determine that offset from aligned terrain or surveyed evidence. The command checks the maximum difference along each rim, removes the terrain beneath the crop, and fills the transition collar. It blends the collar texture and keeps the original wall geometry and atlases, including 8K originals. Terrain image detail still depends on the source imagery.

The output directory contains `cliff.glb`, `cliff.geo.json`, and `terrain-stitch.json`. A failed seam leaves the destination absent.

### Project image geometry

Run through Mesh or Export, then project marked image pixels onto the saved mesh.

```bash
"$ECHO_CLI" --project "$project" project-image-geometry --run "$run_id" \
  --image-id "$capture_id" --geometry marks.json --output projected.json
```

```json
{
  "features": [
    {"id": "pin", "geometry": "point", "pixels": [[620, 285]]},
    {"id": "edge", "geometry": "polyline", "pixels": [[620, 285], [672, 310]]},
    {"id": "outline", "geometry": "polygon", "pixels": [[620, 285], [672, 310], [650, 350]]}
  ]
}
```

Pixels use the full-resolution, orientation-corrected image, with the origin at its top left. Use `--image FILE` to fit an external photo against registered captures. Limit matching to overlapping captures with `--reference-image-ids ID,ID`, or supply at least six well-distributed `controlPoints` in the geometry document. Each control has a `pixel` pair and a model-frame `position` triple. Use `--focal-pixels` for controls on a plane.

The sidecar contains model XYZ points, surface normals, breaks and review flags. Misses have a null position. Pass `--review-image-ids ID,ID` to include corresponding pixels and distance to the first mesh hit from other registered views.
