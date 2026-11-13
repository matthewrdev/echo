#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_dir"
dotnet build Echo.slnx -c Release --nologo -m:1
dotnet tests/Echo.Tests/bin/Release/net10.0/Echo.Tests.dll
if [[ "$(uname -s)" == "Darwin" ]]; then
  swift build --package-path macos -c release
  swift test --package-path macos
fi
