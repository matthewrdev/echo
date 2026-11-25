#!/usr/bin/env bash
set -euo pipefail

output_dir="$1"
mkdir -p "$output_dir"
work_dir="$(mktemp -d "$output_dir/draco.XXXXXXXX")"
trap 'rm -rf "$work_dir"' EXIT

compile() {
    local source="$1"
    local object="${source//\//_}"
    xcrun clang++ -std=c++17 -O3 -DNDEBUG -fvisibility=hidden \
        -arch arm64 -arch x86_64 -mmacosx-version-min=13.0 \
        -Wno-deprecated-declarations -Wno-implicit-const-int-float-conversion \
        -I Native/Draco -c "$source" -o "$work_dir/$object.o"
}

export -f compile
export work_dir
{
    printf '%s\n' Native/EchoDraco.cpp
    sed 's,^,Native/Draco/,' Native/Draco/sources.txt
} | xargs -P 8 -n 1 bash -c 'set -euo pipefail; compile "$1"' _

xcrun clang++ -dynamiclib -arch arm64 -arch x86_64 -mmacosx-version-min=13.0 \
    -install_name @rpath/libecho-draco.dylib "$work_dir"/*.o -o "$work_dir/libecho-draco.dylib"
mv "$work_dir/libecho-draco.dylib" "$output_dir/libecho-draco.dylib"
