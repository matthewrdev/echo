#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == '--help' || "${1:-}" == '-h' ]]; then
    cat <<'HELP'
Usage: NOTARYTOOL_PROFILE=echo-notary scripts/notarize-mac.sh [path/to/Echo.app]

Submit a Developer ID-signed app to Apple, staple its accepted ticket, verify
Gatekeeper acceptance and create a ZIP alongside the app. Credentials stay in
Keychain. To create a profile interactively (use the signing identity's team):

  xcrun notarytool store-credentials echo-notary

Notarisation results and Apple's log are saved beside the app. If the wait times
out, use the submission ID with notarytool info/wait/log to inspect the request.
HELP
    exit 0
fi

repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
app="${1:-$repo_dir/dist/Echo.app}"
profile="${NOTARYTOOL_PROFILE:-}"
if [[ $# -gt 1 || "$(uname -s)" != 'Darwin' ]]; then
    echo 'Run scripts/notarize-mac.sh --help for usage. Notarisation requires macOS.' >&2
    exit 2
fi
if [[ -z "$profile" ]]; then
    echo 'Set NOTARYTOOL_PROFILE to a stored Keychain profile. See scripts/notarize-mac.sh --help.' >&2
    exit 2
fi
if [[ "$app" != *.app || ! -d "$app" ]]; then
    echo "Expected an existing .app bundle: $app" >&2
    exit 2
fi

codesign --verify --deep --strict --all-architectures "$app"
signature="$(codesign -dv --verbose=4 "$app" 2>&1)"
if [[ "$signature" != *'Authority=Developer ID Application: '* || "$signature" != *'flags='*'(runtime)'* ]]; then
    echo 'Notarisation requires a Developer ID Application signature with hardened runtime.' >&2
    exit 1
fi

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
result_path="${app%.app}.notarization.json"
log_path="${app%.app}.notarization-log.json"
archive_path="${app%.app}.zip"
ditto -c -k --keepParent "$app" "$scratch/submission.zip"
echo "Submitting $app for notarisation..."
submit_status=0
xcrun notarytool submit "$scratch/submission.zip" --keychain-profile "$profile" \
    --wait --timeout 30m --output-format json > "$result_path" || submit_status=$?
cat "$result_path"

# notarytool can return a result for a rejected submission. Inspect its status
# explicitly and retain the server log before treating the app as distributable.
submission_id="$(python3 - "$result_path" <<'PY'
import json
import sys

try:
    with open(sys.argv[1]) as file:
        result = json.load(file)
    print(result.get('id', ''))
except json.JSONDecodeError:
    # Authentication and transport errors may only produce stderr.
    print('')
PY
)"
if [[ -n "$submission_id" ]]; then
    xcrun notarytool log "$submission_id" --keychain-profile "$profile" "$log_path" || \
        echo "No log available yet. Inspect submission $submission_id with notarytool info/wait/log." >&2
fi
if [[ "$submit_status" -ne 0 ]]; then
    echo "Notarisation did not complete successfully. See $result_path and any server log." >&2
    exit "$submit_status"
fi
python3 - "$result_path" <<'PY'
import json
import sys

with open(sys.argv[1]) as file:
    result = json.load(file)
if result.get('status') != 'Accepted':
    sys.exit(f"Notarisation was not accepted: {result.get('status', 'unknown')}. See the notarisation log.")
PY

xcrun stapler staple "$app"
xcrun stapler validate "$app"
codesign --verify --deep --strict --all-architectures "$app"
spctl --assess --type execute --verbose=2 "$app"

# ZIPs cannot be stapled: archive the app again after attaching the ticket.
ditto -c -k --keepParent "$app" "$scratch/distribution.zip"
mv -f "$scratch/distribution.zip" "$archive_path"
echo "Notarised app: $app"
echo "Distribution archive: $archive_path"
