#!/usr/bin/env bash
# Apply a staged upgrade after the Host process has exited.
# Usage: apply-upgrade.sh --wait-pid PID --install DIR --data DIR
# Limitations are documented in docs/upgrade.md. This script does not run inside Docker
# as the supported path; replace the image instead.
set -euo pipefail

PID=""
INSTALL=""
DATA=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --wait-pid) PID="$2"; shift 2 ;;
    --install) INSTALL="$2"; shift 2 ;;
    --data) DATA="$2"; shift 2 ;;
    --if-staged) DATA="${DATA:-${HOST_DATA:-/var/lib/iot-daq-gateway}}"; INSTALL="${INSTALL:-/opt/iot-daq-gateway}"; shift ;;
    *) echo "unknown arg $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$DATA" || -z "$INSTALL" ]]; then
  echo "need --install and --data" >&2
  exit 2
fi

STATE="$DATA/upgrade/state.json"
PKG="$DATA/upgrade/package.zip"
if [[ ! -f "$STATE" || ! -f "$PKG" ]]; then
  exit 0
fi

PHASE="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8")).get("phase",""))' "$STATE")"
case "$PHASE" in
  apply-requested|staged|applying) ;;
  *) exit 0 ;;
esac

if [[ -n "$PID" ]]; then
  for _ in $(seq 1 60); do
    if ! kill -0 "$PID" 2>/dev/null; then
      break
    fi
    sleep 1
  done
  if kill -0 "$PID" 2>/dev/null; then
    echo "host still running; not replacing files" >&2
    exit 1
  fi
fi

PREV="$DATA/upgrade/previous"
rm -rf "$PREV"
mkdir -p "$PREV"
# Keep data/ out of the binary backup. A full disk should fail this copy before we delete anything.
tar -C "$INSTALL" --exclude data -cf - . | tar -C "$PREV" -xf -
python3 - "$STATE" <<'PY'
import json, sys
path = sys.argv[1]
state = json.load(open(path, encoding="utf-8"))
state["phase"] = "applying"
state["message"] = "脚本正在替换程序文件。"
json.dump(state, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
PY

if ! unzip -o "$PKG" -d "$INSTALL" -x "data/*"; then
  echo "extract failed, restoring previous binaries" >&2
  tar -C "$PREV" -cf - . | tar -C "$INSTALL" -xf -
  exit 1
fi

if command -v systemctl >/dev/null 2>&1; then
  systemctl start iot-daq-gateway || true
fi
