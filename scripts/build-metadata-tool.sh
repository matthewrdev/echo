#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
runtime="${1:-osx-arm64}"
case "$runtime" in
    osx-arm64) architecture=arm64 ;;
    osx-x64) architecture=x86_64 ;;
    *) echo 'Use osx-arm64 or osx-x64' >&2; exit 2 ;;
esac

# Pinned upstream source archives. No Homebrew or system Perl is used at runtime.
perl_version=5.42.3
exiftool_version=13.59
perl_sha=1137740985837b5cdf15f0cfab932279dcb4352f912fed6fc144e8b4f0823627
exiftool_sha=87d3317882fdae9cb4dcfe57a96a378d0132ffc02c731315bf128b19ddcf7aac
cache="$repo_dir/.build/metadata"
bundle="$cache/$runtime"
stamp="$perl_version/$exiftool_version/$architecture/1"
if [[ -f "$bundle/build-stamp" && "$(cat "$bundle/build-stamp")" == "$stamp" ]]; then
    test "$(env -i PATH=/nonexistent "$bundle/exiftool" -ver)" = "$exiftool_version"
    echo "$bundle"
    exit 0
fi

mkdir -p "$cache/downloads"
download() {
    local url="$1" archive="$2" checksum="$3"
    if [[ ! -f "$archive" ]]; then
        curl -fL --retry 2 "$url" -o "$archive.partial" >&2
        mv "$archive.partial" "$archive"
    fi
    echo "$checksum  $archive" | shasum -a 256 -c - >&2
}
perl_archive="$cache/downloads/perl-$perl_version.tar.gz"
exiftool_archive="$cache/downloads/exiftool-$exiftool_version.tar.gz"
download "https://www.cpan.org/src/5.0/perl-$perl_version.tar.gz" "$perl_archive" "$perl_sha"
download "https://codeload.github.com/exiftool/exiftool/tar.gz/refs/tags/$exiftool_version" "$exiftool_archive" "$exiftool_sha"

work="$(mktemp -d "$cache/build-$runtime.XXXXXX")"
trap 'rm -rf "$work"' EXIT
tar -xzf "$perl_archive" -C "$work"
tar -xzf "$exiftool_archive" -C "$work"
(
    cd "$work/perl-$perl_version"
    # Relocate module lookup with the interpreter, and link libperl into it.
    sh Configure -des -Dprefix="$work/install" -Duserelocatableinc -Uuseshrplib \
        -Dcc=/usr/bin/clang -Dlocincpth= -Dloclibpth= \
        -Accflags="-arch $architecture -mmacosx-version-min=14.0" \
        -Aldflags="-arch $architecture -mmacosx-version-min=14.0" \
        -Dman1dir=none -Dman3dir=none
    make -j "${ECHO_BUILD_JOBS:-8}"
    make install
) >&2

staged="$work/metadata"
mkdir -p "$staged/perl/bin" "$staged/tool" "$staged/licenses" "$staged/sources"
cp "$work/install/bin/perl" "$staged/perl/bin/perl"
cp -R "$work/install/lib" "$staged/perl/lib"
cp "$work/exiftool-$exiftool_version/exiftool" "$staged/tool/exiftool"
cp -R "$work/exiftool-$exiftool_version/lib" "$staged/tool/lib"
cp "$work/exiftool-$exiftool_version/LICENSE" "$staged/licenses/ExifTool.txt"
cp "$work/perl-$perl_version/Artistic" "$staged/licenses/Perl-Artistic.txt"
cp "$work/perl-$perl_version/Copying" "$staged/licenses/Perl-Copying.txt"
cp "$perl_archive" "$exiftool_archive" "$staged/sources/"
cat > "$staged/exiftool" <<'LAUNCHER'
#!/bin/sh
set -eu
metadata_dir=$(CDPATH= cd -- "${0%/*}" && pwd)
unset PERL5LIB PERLLIB PERL5OPT PERL_LOCAL_LIB_ROOT PERL_MB_OPT PERL_MM_OPT
exec "$metadata_dir/perl/bin/perl" "$metadata_dir/tool/exiftool" -config '' "$@"
LAUNCHER
chmod +x "$staged/exiftool"
cat > "$staged/versions.json" <<VERSIONS
{"exifTool":"$exiftool_version","perl":"$perl_version","architecture":"$architecture"}
VERSIONS

# Fail packaging if any executable or extension depends on a developer's install.
while IFS= read -r -d '' binary; do
    if file "$binary" | grep -q 'Mach-O'; then
        if otool -L "$binary" | tail -n +2 | awk '{print $1}' | grep -Ev '^(/usr/lib/|/System/Library/|@)' >&2; then
            echo "Non-system library dependency in $binary" >&2
            exit 1
        fi
        codesign --force --sign - "$binary" >&2
    fi
done < <(find "$staged/perl" -type f \( -name perl -o -name '*.bundle' -o -name '*.dylib' \) -print0)

# This relocated smoke test has no PATH or Perl environment to fall back to.
test "$(env -i PATH=/nonexistent "$staged/exiftool" -ver)" = "$exiftool_version"
echo "$stamp" > "$staged/build-stamp"
mkdir -p "$bundle"
rsync -a --delete "$staged/" "$bundle/"
echo "$bundle"
