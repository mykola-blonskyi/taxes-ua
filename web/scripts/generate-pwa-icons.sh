#!/usr/bin/env bash
# Requires macOS `sips`; rerun on a Mac when the source SVG changes.
set -euo pipefail

cd "$(dirname "$0")/.."
svg="scripts/icon.svg"
out_dir="public/icons"
tmp_png="$(mktemp -t pwa-icon).png"

mkdir -p "$out_dir"
sips -s format png "$svg" --out "$tmp_png" >/dev/null
sips -z 512 512 "$tmp_png" --out "$out_dir/icon-512.png" >/dev/null
sips -z 192 192 "$tmp_png" --out "$out_dir/icon-192.png" >/dev/null
sips -z 180 180 "$tmp_png" --out "public/apple-touch-icon.png" >/dev/null
rm -f "$tmp_png"

echo "Wrote $out_dir/icon-512.png, $out_dir/icon-192.png, public/apple-touch-icon.png"
