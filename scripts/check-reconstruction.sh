#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
if [[ $# -lt 2 ]]; then
  echo "Usage: scripts/check-reconstruction.sh ASSETS_ROOT NEW_OUTPUT [can-lolo|northern-buttress|je-taime|all] [--memory-mib N] [--max-seconds N] [--max-peak-mib N]" >&2
  exit 2
fi
assets_root="$1"
output_root="$2"
shift 2
fixture="can-lolo"
if [[ $# -gt 0 && "$1" != --* ]]; then
  fixture="$1"
  shift
fi
case "$fixture" in
  can-lolo|northern-buttress|je-taime) cases=("$fixture") ;;
  all) cases=(can-lolo northern-buttress je-taime) ;;
  *) echo "Unknown reconstruction case: $fixture" >&2; exit 2 ;;
esac
if [[ -e "$output_root" ]]; then
  echo "Output already exists; choose a fresh directory." >&2
  exit 2
fi
dotnet build "$repo_dir/tools/Echo.Validation" -c Release --nologo -m:1
mkdir -p "$output_root"
status=0
for name in "${cases[@]}"; do
  # One process per case keeps peak memory and runtime measurements independent.
  dotnet "$repo_dir/tools/Echo.Validation/bin/Release/net10.0/Echo.Validation.dll" run \
    "$repo_dir/tests/Echo.Tests/Fixtures/reconstruction/$name.json" \
    "$assets_root" "$output_root/$name" "$@" || status=$?
  if [[ "$status" == 130 ]]; then
    exit 130
  fi
done
exit "$status"
