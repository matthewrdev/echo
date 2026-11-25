#!/usr/bin/env bash
set -euo pipefail
output_dir="$1"
mkdir -p "$output_dir"
for architecture in arm64 x86_64; do
    xcrun swiftc -O -emit-library -module-cache-path "$output_dir/module-cache" \
        -target "$architecture-apple-macos13.0" \
        Native/MetalDepthBridge.swift Native/MetalDepthSession.swift Native/MetalDepthBatch.swift Native/MetalDepthPipeline.swift \
        -o "$output_dir/libEchoDepthMetal-$architecture.dylib"
done
xcrun lipo -create "$output_dir/libEchoDepthMetal-arm64.dylib" \
    "$output_dir/libEchoDepthMetal-x86_64.dylib" -output "$output_dir/libEchoDepthMetal.dylib"
