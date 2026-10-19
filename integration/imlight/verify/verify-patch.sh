#!/usr/bin/env bash
# Verify the Imlight integration patches without Wizard101 data or Imcodec type dumps:
#   1. clone Imlight at the pinned commit (or IMLIGHT_COMMIT) and apply the patches,
#   2. compile the real patched Director Program.cs + new CoreLib files against functional stubs,
#   3. boot the real Program.Main with the example mods and a mod that registers a message service,
#   4. drive the real patched CharacterService / GameTransitionService / MinigameService with a recording stub
#      session (Scenarios.cs), then assert the loader report, lifecycle events, service merging and scenario
#      results listed in expected-output.txt.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
VERIFY_DIR="$REPO_ROOT/integration/imlight/verify"
COMMIT="${IMLIGHT_COMMIT:-b07987168fcfa2d123a5d78df90ca0c76405e427}"
IMLIGHT_DIR="${IMLIGHT_DIR:-$REPO_ROOT/external/Imlight-verify-${COMMIT:0:12}}"
CONFIGURATION="${CONFIGURATION:-Debug}"

IMLIGHT_COMMIT="$COMMIT" "$REPO_ROOT/integration/imlight/setup-imlight.sh" --no-submodules "$IMLIGHT_DIR"

dotnet build "$REPO_ROOT/examples/HelloSpiral/HelloSpiral.csproj" -c "$CONFIGURATION" -v quiet -nologo
dotnet build "$VERIFY_DIR/VerifyMod/VerifyMod.csproj" -c "$CONFIGURATION" -v quiet -nologo -p:ImlightSourceRoot="$IMLIGHT_DIR"

RUN_DIR="$VERIFY_DIR/bin/$CONFIGURATION/net10.0"
rm -rf "$RUN_DIR/mods" "$RUN_DIR/moddata"
mkdir -p "$RUN_DIR/mods"
cp -R "$REPO_ROOT/artifacts/mods/hello-spiral" "$RUN_DIR/mods/"
cp -R "$VERIFY_DIR/bin/mods/imlight-verify" "$RUN_DIR/mods/"

OUTPUT="$(mktemp)"
# Program.Main never returns (it idles forever once the server is up), so stop it after startup.
( cd "$RUN_DIR" && timeout 60 dotnet Imlight.Director.Verify.dll >"$OUTPUT" 2>&1 ) || true

status=0
expect() {
    if grep -qF -- "$1" "$OUTPUT"; then
        echo "  ok   $1"
    else
        echo "  FAIL $1"
        status=1
    fi
}

echo "Checking patched Director startup ($COMMIT):"
while IFS= read -r line || [ -n "$line" ]; do
    line="${line%$'\r'}"
    [ -z "$line" ] && continue
    expect "$line"
done <"$VERIFY_DIR/expected-output.txt"
if [ ! -f "$RUN_DIR/moddata/default/hello-spiral/started.txt" ]; then
    echo "  FAIL hello-spiral wrote moddata/default/hello-spiral/started.txt"
    status=1
else
    echo "  ok   hello-spiral wrote moddata/default/hello-spiral/started.txt"
fi

if [ "$status" != 0 ]; then
    echo "--- Director output ---"
    cat "$OUTPUT"
fi
rm -f "$OUTPUT"
exit "$status"
