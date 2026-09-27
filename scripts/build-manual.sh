#!/usr/bin/env bash
# Build the Chinese user manual. PDF is the artifact; HTML is the intermediate.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/manual"
mkdir -p "$OUT"
SRC="$ROOT/docs/manual/用户手册.md"
HTML="$OUT/用户手册.html"
PDF="$OUT/用户手册.pdf"

pandoc "$SRC" \
  --standalone \
  --metadata title="采集网关用户手册" \
  --metadata lang=zh-CN \
  -o "$HTML"

if command -v weasyprint >/dev/null 2>&1; then
  weasyprint "$HTML" "$PDF"
elif command -v wkhtmltopdf >/dev/null 2>&1; then
  wkhtmltopdf "$HTML" "$PDF"
else
  echo "No PDF engine (weasyprint or wkhtmltopdf). HTML is at $HTML" >&2
  exit 1
fi

echo "Wrote $PDF"
