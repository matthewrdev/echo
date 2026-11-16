#!/usr/bin/env bash
set -euo pipefail
output_dir="$1"
mkdir -p "$output_dir"
for architecture in arm64 x86_64; do
    xcrun swiftc -O -emit-library -module-cache-path "$output_dir/module-cache" \
        -target "$architecture-apple-macos13.0" \
        Native/MetalTextureBridge.swift Native/MetalTextureSession.swift \
        -o "$output_dir/libEchoTextureMetal-$architecture.dylib"
done
xcrun lipo -create "$output_dir/libEchoTextureMetal-arm64.dylib" \
    "$output_dir/libEchoTextureMetal-x86_64.dylib" -output "$output_dir/libEchoTextureMetal.dylib"
