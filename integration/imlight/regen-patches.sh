#!/usr/bin/env bash
# Regenerate integration/imlight/patches from a checkout made by setup-imlight.sh (branch "spiralloader").
#
#   integration/imlight/regen-patches.sh [imlight-checkout]
#
# Deletes the old series first so numbering stays contiguous.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DEST="${1:-$REPO_ROOT/external/Imlight}"
BASE="b07987168fcfa2d123a5d78df90ca0c76405e427"

rm -f "$REPO_ROOT"/integration/imlight/patches/*.patch
git -C "$DEST" format-patch --quiet "$BASE..HEAD" -o "$REPO_ROOT/integration/imlight/patches/"
ls "$REPO_ROOT"/integration/imlight/patches/*.patch
