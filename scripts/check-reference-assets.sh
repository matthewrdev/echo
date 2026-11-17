#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
assets_root="${1:-${ECHO_REFERENCE_ASSETS_ROOT:-}}"
if [[ -z "$assets_root" ]]; then
  echo "Usage: scripts/check-reference-assets.sh /path/to/3d-assets" >&2
  exit 2
fi
dotnet run --project "$repo_dir/tests/Echo.Tests" -c Release -- --reference-assets "$assets_root"
