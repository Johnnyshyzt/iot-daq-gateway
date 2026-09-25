#!/usr/bin/env bash
# Cross-compile a versioned Windows x64 self-contained zip. No Fwlib64.dll is packed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="${DOTNET_ROOT}:${PATH}"

CONFIGURATION="${CONFIGURATION:-Release}"
RID="win-x64"

if [[ ! -f "$ROOT/src/Web/dist/index.html" ]]; then
  echo "Building src/Web ..."
  (cd "$ROOT/src/Web" && npm ci && npm run build)
fi

VERSION="$(dotnet msbuild src/Host/Host.csproj -nologo -getProperty:Version | tr -d '\r' | tail -n 1 | xargs)"
if [[ -z "$VERSION" ]]; then
  echo "Could not read Version from Host.csproj" >&2
  exit 1
fi

SHA=""
if git rev-parse --short HEAD >/dev/null 2>&1; then
  SHA="$(git rev-parse --short HEAD)"
fi

INFORMATIONAL="$VERSION"
if [[ -n "$SHA" ]]; then
  INFORMATIONAL="${VERSION}+${SHA}"
fi

STAGE="$ROOT/artifacts/${RID}/payload"
DIST="$ROOT/artifacts/${RID}"
FOLDER="iot-daq-gateway-${VERSION}-${RID}"
ZIP="${DIST}/${FOLDER}.zip"

rm -rf "$STAGE"
mkdir -p "$STAGE" "$DIST"

echo "Publishing Host $INFORMATIONAL ($RID self-contained)..."
dotnet publish src/Host/Host.csproj \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  --nologo \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:InformationalVersion="$INFORMATIONAL" \
  -o "$STAGE"

# Replace the developer Fake copy with the site FOCAS template.
cp -f "$ROOT/configs/examples/gateway.windows.yaml" "$STAGE/gateway.yaml"
cp -f "$ROOT/configs/examples/gateway.yaml" "$STAGE/gateway.fake.yaml"
cp -f "$ROOT/configs/examples/gateway.focas.yaml" "$STAGE/gateway.focas.yaml"
rm -f "$STAGE/appsettings.Development.json"

printf '%s\n' "$INFORMATIONAL" > "$STAGE/VERSION.txt"

cp -f "$ROOT/packaging/windows/install-service.bat" "$STAGE/"
cp -f "$ROOT/packaging/windows/uninstall-service.bat" "$STAGE/"
cp -f "$ROOT/packaging/windows/run-console.bat" "$STAGE/"
cp -f "$ROOT/packaging/windows/service-env.ps1" "$STAGE/"
cp -f "$ROOT/packaging/windows/service.env.example" "$STAGE/"
cp -f "$ROOT/packaging/windows/Fwlib64.dll.PLACE_HERE.txt" "$STAGE/"
cp -f "$ROOT/packaging/windows/安装说明.txt" "$STAGE/"
cp -f "$ROOT/packaging/windows/appsettings.Field.json" "$STAGE/appsettings.json"

# cmd.exe on factory PCs expects CRLF.
python3 - "$STAGE" <<'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
for path in root.glob("*.bat"):
    data = path.read_bytes().replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    path.write_bytes(data)
txt = root / "安装说明.txt"
if txt.exists():
    data = txt.read_bytes().replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    if not data.startswith(b"\xef\xbb\xbf"):
        data = b"\xef\xbb\xbf" + data
    txt.write_bytes(data)
PY

if [[ -e "$STAGE/Fwlib64.dll" || -e "$STAGE/fwlib64.dll" ]]; then
  echo "Refusing to pack vendor FOCAS binaries." >&2
  exit 1
fi

if [[ ! -f "$STAGE/Host.exe" ]]; then
  echo "Publish did not produce Host.exe" >&2
  exit 1
fi
if [[ ! -f "$STAGE/wwwroot/index.html" ]]; then
  echo "Publish did not include wwwroot/index.html. Build src/Web first." >&2
  exit 1
fi
if [[ ! -f "$STAGE/data/seed/gateway.yaml" ]]; then
  echo "Publish did not include data/seed/gateway.yaml." >&2
  exit 1
fi
if [[ ! -f "$STAGE/service.env.example" || ! -f "$STAGE/service-env.ps1" ]]; then
  echo "Publish stage is missing service.env.example or service-env.ps1." >&2
  exit 1
fi

rm -f "$ZIP"
(
  cd "$DIST"
  rm -rf "$FOLDER"
  mkdir -p "$FOLDER"
  # payload/* into versioned folder
  cp -a payload/. "$FOLDER/"
  python3 -m zipfile -c "$ZIP" "$FOLDER"
  rm -rf "$FOLDER"
)

echo "Wrote $ZIP"
python3 - "$ZIP" <<'PY'
import sys, zipfile
z = zipfile.ZipFile(sys.argv[1])
names = z.namelist()
required = [
    "Host.exe",
    "wwwroot/index.html",
    "gateway.yaml",
    "install-service.bat",
    "uninstall-service.bat",
    "run-console.bat",
    "Fwlib64.dll.PLACE_HERE.txt",
    "安装说明.txt",
    "VERSION.txt",
    "data/seed/gateway.yaml",
    "data/seed/sinks/mqtt.yaml",
    "service.env.example",
    "service-env.ps1",
    "appsettings.json",
]
missing = []
for item in required:
    if not any(n.replace("\\", "/").endswith(item) for n in names):
        missing.append(item)
banned = [n for n in names if n.lower().endswith("fwlib64.dll")]
if missing:
    raise SystemExit("zip missing: " + ", ".join(missing))
if banned:
    raise SystemExit("zip contains vendor DLL: " + ", ".join(banned))
settings = [n for n in names if n.replace("\\", "/").endswith("appsettings.json")]
if len(settings) != 1:
    raise SystemExit("zip must contain exactly one appsettings.json")
text = z.read(settings[0]).decode("utf-8")
if '"AccountMode": "field"' not in text:
    raise SystemExit("field appsettings.json is missing AccountMode=field")
if '"Password"' in text:
    raise SystemExit("field appsettings.json still contains demo passwords")
print(f"zip entries: {len(names)}")
PY
