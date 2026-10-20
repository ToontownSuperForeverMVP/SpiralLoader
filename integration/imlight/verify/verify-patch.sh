#!/usr/bin/env bash
# Verify the Imlight integration patches without Wizard101 data or Imcodec type dumps:
#   1. clone Imlight at the pinned commit (or IMLIGHT_COMMIT) and apply the patches,
#   2. compile the real patched Director Program.cs, the modding files, the real session plumbing
#      (SessionActor, MessageService, dispatcher) and the hook call-site services against functional stubs,
#   3. build the example mod and the separately compiled verification mods,
#   4. "modded" run: boot the real Program.Main with every mod (FailurePolicy = Continue, so the duplicate-claim
#      mod's failure is reported instead of aborting) and drive real sessions with synthetic client packets
#      (Scenarios.cs); assert expected-output.txt plus expected-output-<commit12>.txt when present,
#   5. "vanilla" run: the same with the client-process mods disabled; assert expected-vanilla-output.txt.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
VERIFY_DIR="$REPO_ROOT/integration/imlight/verify"
COMMIT="${IMLIGHT_COMMIT:-b07987168fcfa2d123a5d78df90ca0c76405e427}"
IMLIGHT_DIR="${IMLIGHT_DIR:-$REPO_ROOT/external/Imlight-verify-${COMMIT:0:12}}"
CONFIGURATION="${CONFIGURATION:-Debug}"
VERIFY_MODS=(VerifyMod ClientProcessMod IsolationMod DuplicateClaimMod)
CLIENT_PROCESS_MODS="client-process-verify, isolation-verify, duplicate-claim-verify"

IMLIGHT_COMMIT="$COMMIT" "$REPO_ROOT/integration/imlight/setup-imlight.sh" --no-submodules "$IMLIGHT_DIR"

dotnet build "$REPO_ROOT/examples/HelloSpiral/HelloSpiral.csproj" -c "$CONFIGURATION" -v quiet -nologo
for mod in "${VERIFY_MODS[@]}"; do
    dotnet build "$VERIFY_DIR/$mod/$mod.csproj" -c "$CONFIGURATION" -v quiet -nologo -p:ImlightSourceRoot="$IMLIGHT_DIR"
done

RUN_DIR="$VERIFY_DIR/bin/$CONFIGURATION/net10.0"
INI="$RUN_DIR/Config/Imlight.ini"
cp "$IMLIGHT_DIR/src/Imlight.Director/Config/Imlight.ini" "$INI.original"

# Boots the Director with the given [SpiralLoader] FailurePolicy and DisabledMods; output goes to $1.
run_director() {
    local output="$1" policy="$2" disabled="$3"
    rm -rf "$RUN_DIR/mods" "$RUN_DIR/moddata"
    mkdir -p "$RUN_DIR/mods"
    cp -R "$REPO_ROOT/artifacts/mods/hello-spiral" "$RUN_DIR/mods/"
    cp -R "$VERIFY_DIR/bin/mods/"* "$RUN_DIR/mods/"
    sed -e "s/^FailurePolicy = .*/FailurePolicy = $policy/" -e "s/^DisabledMods =.*/DisabledMods = $disabled/" \
        "$INI.original" >"$INI"

    # Program.Main never returns (it idles once the server is up): stop it shortly after the scenarios finish
    # (the game servers report right after them), or after 90 s.
    ( cd "$RUN_DIR" && exec dotnet Imlight.Director.Verify.dll ) >"$output" 2>&1 &
    local pid=$! waited=0
    while kill -0 "$pid" 2>/dev/null && [ "$waited" -lt 90 ]; do
        if grep -q "VERIFY: scenarios done" "$output"; then
            sleep 3
            break
        fi
        sleep 1
        waited=$((waited + 1))
    done
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
}

status=0
# Asserts every non-empty line of the given expectation files appears in the output; a line starting with "!"
# must NOT appear.
check() {
    local output="$1"; shift
    local file line
    for file in "$@"; do
        [ -f "$file" ] || continue
        while IFS= read -r line || [ -n "$line" ]; do
            line="${line%$'\r'}"
            [ -z "$line" ] && continue
            if [ "${line:0:1}" = "!" ]; then
                if grep -qF -- "${line:1}" "$output"; then
                    echo "  FAIL absent: ${line:1}"
                    status=1
                else
                    echo "  ok   absent: ${line:1}"
                fi
            elif grep -qF -- "$line" "$output"; then
                echo "  ok   $line"
            else
                echo "  FAIL $line"
                status=1
            fi
        done <"$file"
    done
}

MODDED="$(mktemp)"
VANILLA="$(mktemp)"

run_director "$MODDED" Continue ""
echo "Checking patched Director with mods ($COMMIT):"
check "$MODDED" "$VERIFY_DIR/expected-output.txt" "$VERIFY_DIR/expected-output-${COMMIT:0:12}.txt"
if [ -f "$RUN_DIR/moddata/default/hello-spiral/started.txt" ]; then
    echo "  ok   hello-spiral wrote moddata/default/hello-spiral/started.txt"
else
    echo "  FAIL hello-spiral wrote moddata/default/hello-spiral/started.txt"
    status=1
fi
modded_status=$status

run_director "$VANILLA" Abort "$CLIENT_PROCESS_MODS"
echo "Checking patched Director with the client-process mods disabled ($COMMIT):"
check "$VANILLA" "$VERIFY_DIR/expected-vanilla-output.txt"

cp "$INI.original" "$INI"
if [ "$modded_status" != 0 ]; then
    echo "--- Director output (mods) ---"
    cat "$MODDED"
fi
if [ "$status" != 0 ] && [ "$modded_status" = 0 ]; then
    echo "--- Director output (client-process mods disabled) ---"
    cat "$VANILLA"
fi
rm -f "$MODDED" "$VANILLA" "$INI.original"
exit "$status"
