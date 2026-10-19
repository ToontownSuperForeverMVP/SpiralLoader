#!/usr/bin/env bash
# Clone Imlight at a pinned commit and apply the SpiralLoader integration patches.
#
#   integration/imlight/setup-imlight.sh [--no-submodules] [destination]
#
# destination defaults to <repo>/external/Imlight. IMLIGHT_COMMIT overrides the commit (default: the commit
# Spiral Shuffle is built on). The checkout gets a local branch "spiralloader" = commit + patches.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMMIT="${IMLIGHT_COMMIT:-b07987168fcfa2d123a5d78df90ca0c76405e427}"
SUBMODULES=1
DEST=""
for arg in "$@"; do
    case "$arg" in
        --no-submodules) SUBMODULES=0 ;;
        -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
        *) DEST="$arg" ;;
    esac
done
DEST="${DEST:-$REPO_ROOT/external/Imlight}"

if [ ! -d "$DEST/.git" ]; then
    git clone https://github.com/Revive101/Imlight.git "$DEST"
fi
if ! git -C "$DEST" diff --quiet || ! git -C "$DEST" diff --cached --quiet; then
    echo "error: $DEST has uncommitted changes; refusing to reset it." >&2
    exit 1
fi
if ! git -C "$DEST" cat-file -e "$COMMIT^{commit}" 2>/dev/null; then
    git -C "$DEST" fetch origin
fi

git -C "$DEST" checkout -q -B spiralloader "$COMMIT"
if [ "$SUBMODULES" = 1 ]; then
    git -C "$DEST" submodule update --init
fi
git -C "$DEST" -c user.name="SpiralLoader" -c user.email="spiralloader@localhost" \
    am --3way --quiet "$REPO_ROOT"/integration/imlight/patches/*.patch

echo "Imlight $COMMIT + $(ls "$REPO_ROOT"/integration/imlight/patches/*.patch | wc -l | tr -d ' ') SpiralLoader patches -> $DEST"
