#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
icon_dir="$(mktemp -d)/Echo.iconset"
mkdir -p "$icon_dir"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$repo_dir/assets/echo-icon.png" --out "$icon_dir/icon_${size}x${size}.png" >/dev/null
  retina=$((size * 2))
  sips -z "$retina" "$retina" "$repo_dir/assets/echo-icon.png" --out "$icon_dir/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$icon_dir" -o "$repo_dir/assets/Echo.icns"
rm -r "${icon_dir%/Echo.iconset}"
