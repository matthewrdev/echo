#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_dir"
dotnet format whitespace Echo.slnx --no-restore --verify-no-changes
dotnet run --project tools/Echo.CodeStyle --no-restore -- "$repo_dir"
xcrun swift-format lint --strict --recursive macos/Sources macos/Tests
