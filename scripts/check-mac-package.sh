#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
app="${1:-$repo_dir/dist/Echo.app}"
photo="${2:-}"
payload_tag="${3:-}"
expected_payload="${4:-}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT

# Relocation, spaces in the app path, and an empty tool search path must all work.
ditto "$app" "$scratch/Echo relocated.app"
cli="$scratch/Echo relocated.app/Contents/MacOS/echo-photo"
run_cli() {
    env -i PATH=/nonexistent HOME="$scratch" TMPDIR="$scratch/" "$cli" "$@"
}
codesign --verify --deep --strict "$scratch/Echo relocated.app"
run_cli doctor > "$scratch/doctor.json"
python3 - "$scratch/doctor.json" <<'PY'
import json, sys
doctor = json.load(open(sys.argv[1]))
assert doctor['metadataReady'] and 'managed C#' in doctor['metadataEngine'], doctor
PY

mkdir "$scratch/photos"
if [[ -n "$photo" ]]; then
    cp "$photo" "$scratch/photos/$(basename "$photo")"
else
    python3 - "$scratch/photos/fixture.png" <<'PY'
import struct, sys, zlib
def chunk(kind, data):
    return struct.pack('!I', len(data)) + kind + data + struct.pack('!I', zlib.crc32(kind + data))
header = struct.pack('!2I5B', 16, 16, 8, 2, 0, 0, 0)
pixels = b''.join(b'\0' + bytes([r * 16, 128, 64]) * 16 for r in range(16))
open(sys.argv[1], 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', header) + chunk(b'IDAT', zlib.compress(pixels)) + chunk(b'IEND', b''))
PY
fi
run_cli project init "$scratch/test.echo" --name 'Bundled metadata check' > /dev/null
run_cli --project "$scratch/test.echo" images import "$scratch/photos" > "$scratch/import.json"
image_id="$(python3 - "$scratch/import.json" <<'PY'
import json, sys
photos = json.load(open(sys.argv[1]))['imported']
assert len(photos) == 1 and photos[0]['width'] > 0 and photos[0]['height'] > 0
print(photos[0]['id'])
PY
)"
run_cli --project "$scratch/test.echo" images metadata "$image_id" > "$scratch/metadata.json"
python3 - "$scratch/metadata.json" <<'PY'
import json, sys
tags = json.load(open(sys.argv[1]))
assert any(key.endswith('ImageWidth') for key in tags), tags
print(f'Imported photo and read {len(tags)} metadata fields without external tools.')
PY

if [[ -n "$payload_tag" ]]; then
    run_cli --project "$scratch/test.echo" images payload "$image_id" "$payload_tag" > "$scratch/payload.json"
    python3 - "$scratch/payload.json" "$expected_payload" <<'PY'
import base64, json, sys
payload = base64.b64decode(json.load(open(sys.argv[1])))
assert payload
if sys.argv[2]:
    assert payload == open(sys.argv[2], 'rb').read()
print(f'Original binary metadata payload preserved: {len(payload)} bytes.')
PY
fi
echo 'Relocated self-contained package check passed.'
