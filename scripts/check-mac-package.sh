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
cli="$scratch/Echo relocated.app/Contents/MacOS/echo"
python3 - "$cli" <<'PY'
import json
import pathlib
import sys

cli = pathlib.Path(sys.argv[1])
contents = cli.parent.parent.resolve()
options = json.loads(cli.with_suffix('.runtimeconfig.json').read_text())['runtimeOptions']
assert not options.get('framework') and not options.get('frameworks'), 'CLI requires an installed .NET runtime.'
assert any(item['name'] == 'Microsoft.NETCore.App' for item in options.get('includedFrameworks', [])), \
    'CLI does not declare a bundled .NET runtime.'
for name in ('libhostfxr.dylib', 'libhostpolicy.dylib', 'libcoreclr.dylib', 'System.Private.CoreLib.dll'):
    library = (cli.parent / name).resolve(strict=True)
    assert contents in library.parents and library.is_file(), f'Runtime dependency is outside the app: {name}'
PY
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
run_cli --project "$scratch/test.echo" images import "$scratch/photos" --events ndjson > "$scratch/import.ndjson"
image_id="$(python3 - "$scratch/import.ndjson" <<'PY'
import json, sys
with open(sys.argv[1]) as events:
    records = [json.loads(line) for line in events]
assert len(records) >= 2 and all(record['schemaVersion'] == 1 for record in records), records
assert records[0]['type'] == 'imageImportStarted' and records[-1]['type'] == 'imageImportCompleted', records
photos = records[-1]['imported']
assert len(photos) == 1 and photos[0]['width'] > 0 and photos[0]['height'] > 0
print(photos[0]['id'])
PY
)"
mkdir -p "$scratch/card/DCIM/100MEDIA"
cp "$scratch/photos/"* "$scratch/card/DCIM/100MEDIA/"
run_cli drone scan --source "$scratch/card" > "$scratch/drone.json"
python3 - "$scratch/drone.json" "$scratch/card/DCIM/100MEDIA" <<'PYTHON'
import json, pathlib, sys
with open(sys.argv[1]) as result:
    scan = json.load(result)
entries = [photo for group in scan['groups'] for photo in group['photos']]
assert scan['schemaVersion'] == 1 and len(entries) == 1, scan
assert pathlib.Path(entries[0]['path']).parent.resolve() == pathlib.Path(sys.argv[2]).resolve(), entries
assert entries[0]['set'], entries
PYTHON

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
python3 - "$scratch" <<'PY'
import json, pathlib, struct, sys

root = pathlib.Path(sys.argv[1])
# A tiny textured triangle exercises the native renderer in the relocated bundle.
image = next((root / 'photos').iterdir()).read_bytes()
if image[:8] != b'\x89PNG\r\n\x1a\n':
    # Optional metadata fixtures can be JPEGs; create a separate known PNG.
    import zlib
    def chunk(kind, data):
        return struct.pack('!I', len(data)) + kind + data + struct.pack('!I', zlib.crc32(kind + data))
    image = (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', 1, 1, 8, 2, 0, 0, 0))
             + chunk(b'IDAT', zlib.compress(b'\0\xff\0\0')) + chunk(b'IEND', b''))
binary = struct.pack('<15f3I', -1, -1, 0, 1, -1, 0, 0, 1, 0, 0, 0, 1, 0, .5, 1, 0, 1, 2) + image
binary += bytes((-len(binary)) % 4)
document = {
    'asset': {'version': '2.0'}, 'buffers': [{'byteLength': len(binary)}],
    'bufferViews': [{'buffer': 0, 'byteOffset': offset, 'byteLength': length}
                    for offset, length in [(0, 36), (36, 24), (60, 12), (72, len(image))]],
    'accessors': [{'bufferView': 0, 'componentType': 5126, 'count': 3, 'type': 'VEC3'},
                  {'bufferView': 1, 'componentType': 5126, 'count': 3, 'type': 'VEC2'},
                  {'bufferView': 2, 'componentType': 5125, 'count': 3, 'type': 'SCALAR'}],
    'meshes': [{'primitives': [{'attributes': {'POSITION': 0, 'TEXCOORD_0': 1}, 'indices': 2, 'material': 0}]}],
    'materials': [{'pbrMetallicRoughness': {'baseColorTexture': {'index': 0}}}],
    'textures': [{'source': 0}], 'images': [{'bufferView': 3, 'mimeType': 'image/png'}],
}
encoded = json.dumps(document).encode()
encoded += b' ' * ((-len(encoded)) % 4)
glb = (struct.pack('<5I', 0x46546c67, 2, 28 + len(encoded) + len(binary), len(encoded), 0x4e4f534a)
       + encoded + struct.pack('<2I', len(binary), 0x004e4942) + binary)
(root / 'render model.glb').write_bytes(glb)
PY
run_cli render "$scratch/render model.glb" --output "$scratch/render preview.png" --width 320 --height 200 > "$scratch/render.json"
python3 - "$scratch" <<'PY'
import json, pathlib, struct, sys
root = pathlib.Path(sys.argv[1])
result = json.loads((root / 'render.json').read_text())
assert result['triangles'] == 1 and result['vertices'] == 3, result
png = (root / 'render preview.png').read_bytes()
assert png[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('!2I', png[16:24]) == (320, 200)
print('Rendered a PNG through the bundled native renderer without external tools.')
PY
echo 'Relocated self-contained package check passed.'
