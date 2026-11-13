#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_dir"
runtime="${1:-osx-arm64}"
case "$runtime" in osx-arm64|osx-x64) ;; *) echo 'Use osx-arm64 or osx-x64' >&2; exit 2 ;; esac
architecture="arm64"
if [[ "$runtime" == "osx-x64" ]]; then architecture="x86_64"; fi
swift build --package-path macos -c release --arch "$architecture"
native_bin="$(swift build --package-path macos -c release --arch "$architecture" --show-bin-path)"
bash scripts/build-icon.sh
staging_dir="$(mktemp -d)"
trap 'rm -rf "$staging_dir"' EXIT
mkdir -p "$staging_dir/Contents/MacOS/cli" "$staging_dir/Contents/Resources"
dotnet publish src/Echo.Cli -c Release -r "$runtime" --self-contained true -p:PublishSingleFile=false -o "$staging_dir/Contents/MacOS/cli" --nologo -m:1
cp "$native_bin/EchoDesktop" "$staging_dir/Contents/MacOS/EchoDesktop"
cp scripts/Info.plist "$staging_dir/Contents/Info.plist"
cp assets/Echo.icns "$staging_dir/Contents/Resources/Echo.icns"
app_dir="$repo_dir/dist/Echo.app"
mkdir -p "$app_dir"
rsync -a --delete "$staging_dir/" "$app_dir/"
codesign --force --deep --sign - "$app_dir"
echo "Built $app_dir (local ad-hoc signature; not notarised)"
