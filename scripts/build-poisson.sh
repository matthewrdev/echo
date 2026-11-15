#!/usr/bin/env bash
set -euo pipefail
output_dir="$1"
mkdir -p "$output_dir"
for architecture in arm64 x86_64; do
    xcrun clang++ -std=c++17 -O3 -DNDEBUG -DFAST_COMPILE -DSANITIZED_PR \
        -arch "$architecture" -mmacosx-version-min=13.0 \
        -Wno-deprecated -Wno-invalid-offset-of -Wno-dangling-else \
        Native/EchoPoisson.cpp -o "$output_dir/echo-poisson-$architecture"
done
xcrun lipo -create "$output_dir/echo-poisson-arm64" "$output_dir/echo-poisson-x86_64" -output "$output_dir/echo-poisson"
