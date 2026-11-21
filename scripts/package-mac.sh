#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    cat <<'HELP'
Usage: scripts/package-mac.sh [osx-arm64|osx-x64] [output/Echo.app]

Build a self-contained app signed for distribution outside the Mac App Store.
CODESIGN_IDENTITY defaults to Developer ID Application: alpha outdoors pty. ltd. (65MZ7WPY52).
Set it to another Developer ID Application name or certificate SHA-1, or '-' for
an ad-hoc local build. Signing failures never fall back to ad-hoc signing.
Set NOTARYTOOL_PROFILE to a stored Keychain profile to also notarise, staple and
create a distribution ZIP. See scripts/notarize-mac.sh --help for credential setup.
HELP
    exit 0
fi

repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_dir"
runtime="${1:-osx-arm64}"
case "$runtime" in osx-arm64|osx-x64) ;; *) echo 'Use osx-arm64 or osx-x64' >&2; exit 2 ;; esac
if [[ $# -gt 2 || "$(uname -s)" != "Darwin" ]]; then
    echo 'Run scripts/package-mac.sh --help for usage. Packaging requires macOS.' >&2
    exit 2
fi

app_dir="${2:-$repo_dir/dist/Echo.app}"
case "$app_dir" in
    /*) ;;
    *) app_dir="$repo_dir/$app_dir" ;;
esac
if [[ "$app_dir" != *.app || -L "$app_dir" ]]; then
    echo 'The output must be an .app directory, not a symbolic link.' >&2
    exit 2
fi

signing_identity="${CODESIGN_IDENTITY-Developer ID Application: alpha outdoors pty. ltd. (65MZ7WPY52)}"
notary_profile="${NOTARYTOOL_PROFILE:-}"
signing_description='Local ad-hoc signature; not notarised'
signing_arguments=(--force --sign "$signing_identity")
if [[ "$signing_identity" == '-' ]]; then
    if [[ -n "$notary_profile" ]]; then
        echo 'Notarisation requires a Developer ID Application identity, not ad-hoc signing.' >&2
        exit 2
    fi
else
    # Resolve an exact Developer ID name or fingerprint before the expensive build.
    # This also rejects Apple Development and App Store distribution identities.
    identities="$(security find-identity -v -p codesigning)"
    signing_hash="$(python3 - "$signing_identity" "$identities" <<'PY'
import re
import sys

requested = sys.argv[1]
matches = {
    fingerprint
    for fingerprint, name in re.findall(r'\b([0-9A-Fa-f]{40}) "([^"]+)"', sys.argv[2])
    if name.startswith('Developer ID Application: ')
    and (requested == name or requested.upper() == fingerprint.upper())
}
if len(matches) != 1:
    sys.exit(f'Expected one valid Developer ID Application identity for {requested!r}. '
             'Check security find-identity -v -p codesigning, or use CODESIGN_IDENTITY=- for a local build.')
print(matches.pop())
PY
)"
    signing_arguments=(--force --sign "$signing_hash" --options runtime --timestamp)
    signing_description='Developer ID signature; hardened runtime'
fi

architecture="arm64"
if [[ "$runtime" == "osx-x64" ]]; then architecture="x86_64"; fi
swift build --package-path macos -c release --arch "$architecture"
native_bin="$(swift build --package-path macos -c release --arch "$architecture" --show-bin-path)"
bash scripts/build-icon.sh
staging_dir="$(mktemp -d)"
trap 'rm -rf "$staging_dir"' EXIT
staged_app="$staging_dir/$(basename "$app_dir")"
processor_dir="$staged_app/Contents/Resources/cli"
mkdir -p "$staged_app/Contents/MacOS" "$staged_app/Contents/Frameworks" "$processor_dir"
python3 - "$staged_app/Contents/Resources/BuildInfo.json" "$runtime" "$signing_description" <<'PY'
import datetime
import json
import subprocess
import sys

def output(*args):
    return subprocess.check_output(args, text=True).strip()

info = {
    "commit": output("git", "rev-parse", "HEAD"),
    "dirty": bool(output("git", "status", "--porcelain", "--untracked-files=normal")),
    "builtAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "swiftCompiler": output("swift", "--version").splitlines()[0],
    "targetRuntime": sys.argv[2],
    "processorDeployment": "Self-contained .NET; JIT enabled; Native AOT disabled",
    "signing": sys.argv[3],
}
with open(sys.argv[1], "w") as file:
    json.dump(info, file, indent=2)
PY
dotnet publish src/Echo.Cli -c Release -r "$runtime" --self-contained true -p:PublishSingleFile=false -p:PublishAot=false -o "$processor_dir" --nologo -m:1

stage_processor() {
    local source
    local name
    local content_type
    # Keep managed assemblies/data in Resources, native libraries in Frameworks,
    # and tools in MacOS. Relative links preserve .NET's adjacent-file lookup
    # whether a loader resolves a path through MacOS or Resources/cli.
    for source in "$processor_dir"/*; do
        name="$(basename "$source")"
        content_type="$(file -b "$source")"
        case "$content_type" in
            Mach-O*'dynamically linked shared library'*)
                mv "$source" "$staged_app/Contents/Frameworks/$name"
                ln -s "../../Frameworks/$name" "$source"
                ln -s "../Frameworks/$name" "$staged_app/Contents/MacOS/$name"
                ;;
            Mach-O*)
                mv "$source" "$staged_app/Contents/MacOS/$name"
                ln -s "../../MacOS/$name" "$source"
                ;;
            *)
                ln -s "../Resources/cli/$name" "$staged_app/Contents/MacOS/$name"
                ;;
        esac
    done
}

stage_processor
cp "$native_bin/EchoDesktop" "$staged_app/Contents/MacOS/EchoDesktop"
cp "$native_bin/EchoRender" "$staged_app/Contents/MacOS/EchoRender"
ln -s "../../MacOS/EchoRender" "$processor_dir/EchoRender"
cp THIRD_PARTY_NOTICES.md "$staged_app/Contents/Resources/THIRD_PARTY_NOTICES.md"
cp -R licenses "$staged_app/Contents/Resources/licenses"
cp scripts/Info.plist "$staged_app/Contents/Info.plist"
cp assets/Echo.icns "$staged_app/Contents/Resources/Echo.icns"

sign_app() {
    local target
    local bundle_identifier
    bundle_identifier="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$staged_app/Contents/Info.plist")"
    find "$staged_app/Contents" -type f -print0 > "$staging_dir/files"
    while IFS= read -r -d '' target; do
        if [[ "$target" == "$staged_app/Contents/MacOS/EchoDesktop" ]]; then
            continue
        fi
        case "$(file -b "$target")" in
            Mach-O*) ;;
            *) continue ;;
        esac

        # Replace vendor signatures and entitlements on every native library and
        # helper. Sign the enclosing app last; --deep is only used to verify.
        if codesign -d "$target" >/dev/null 2>&1; then
            codesign --remove-signature "$target"
        fi
        if [[ "$target" == "$staged_app/Contents/MacOS/echo-photo" ]]; then
            codesign "${signing_arguments[@]}" --identifier "$bundle_identifier.cli" \
                --entitlements scripts/echo-cli.entitlements.plist "$target"
        else
            codesign "${signing_arguments[@]}" "$target"
        fi
        codesign --verify --strict --all-architectures "$target"
    done < "$staging_dir/files"

    codesign "${signing_arguments[@]}" "$staged_app"
    codesign --verify --deep --strict --all-architectures "$staged_app"
}

sign_app
if [[ "$(uname -m)" == "$architecture" ]]; then
    bash scripts/check-mac-package.sh "$staged_app"
else
    echo "Run scripts/check-mac-package.sh on a $architecture Mac to verify the package runtime."
fi
mkdir -p "$app_dir"
rsync -a --delete "$staged_app/" "$app_dir/"
codesign --verify --deep --strict --all-architectures "$app_dir"
if [[ -n "$notary_profile" ]]; then
    bash scripts/notarize-mac.sh "$app_dir"
else
    echo "Built $app_dir ($signing_description; notarisation not requested)"
fi
