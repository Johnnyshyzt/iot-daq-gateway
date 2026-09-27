#!/usr/bin/env bash
# Self-contained linux-x64 tarball. No vendor FOCAS binaries.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="${DOTNET_ROOT}:${PATH}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

CONFIGURATION="${CONFIGURATION:-Release}"
RID="linux-x64"
if [[ ! -f "$ROOT/src/Web/dist/index.html" ]]; then
  (cd "$ROOT/src/Web" && npm ci && npm run build)
fi

VERSION="$(dotnet msbuild src/Host/Host.csproj -nologo -getProperty:Version | tr -d '\r' | tail -n 1 | xargs)"
SHA=""
if git rev-parse --short HEAD >/dev/null 2>&1; then
  SHA="$(git rev-parse --short HEAD)"
fi
INFORMATIONAL="$VERSION"
if [[ -n "$SHA" ]]; then INFORMATIONAL="${VERSION}+${SHA}"; fi

STAGE="$ROOT/artifacts/${RID}/payload"
DIST="$ROOT/artifacts/${RID}"
NAME="iot-daq-gateway-${VERSION}-${RID}"
rm -rf "$STAGE"
mkdir -p "$STAGE" "$DIST"

dotnet publish src/Host/Host.csproj \
  -c "$CONFIGURATION" -r "$RID" --self-contained true --nologo \
  -p:PublishSingleFile=false -p:PublishTrimmed=false \
  -p:DebugType=None -p:DebugSymbols=false \
  -p:InformationalVersion="$INFORMATIONAL" \
  -o "$STAGE"

rm -f "$STAGE/appsettings.Development.json"
cp -f "$ROOT/packaging/windows/appsettings.Field.json" "$STAGE/appsettings.json"
cp -f "$ROOT/packaging/linux/iot-daq-gateway.service" "$STAGE/"
cp -f "$ROOT/packaging/linux/apply-upgrade.sh" "$STAGE/"
chmod +x "$STAGE/apply-upgrade.sh" "$STAGE/Host" || true
printf '%s\n' "$INFORMATIONAL" > "$STAGE/VERSION.txt"

if [[ -e "$STAGE/libfwlib32.so" ]]; then
  echo "Refusing to pack vendor FOCAS binaries." >&2
  exit 1
fi

tar -C "$STAGE" -czf "$DIST/${NAME}.tar.gz" .
echo "Wrote $DIST/${NAME}.tar.gz"
