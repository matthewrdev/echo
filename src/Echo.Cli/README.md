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
