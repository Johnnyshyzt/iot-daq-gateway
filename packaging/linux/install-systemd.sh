#!/usr/bin/env bash
# Install the framework-dependent Host as a systemd service.
# Publish first: see docs/linux-install.md. Requires root.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ "$(id -u)" -ne 0 ]]; then
  echo "请用 root 运行：sudo $0" >&2
  exit 1
fi

if [[ ! -f /opt/iot-daq-gateway/Host.dll ]]; then
  echo "先把 dotnet publish 的输出放到 /opt/iot-daq-gateway（目录里要有 Host.dll 和 wwwroot）。" >&2
  exit 1
fi

if ! id iot-daq >/dev/null 2>&1; then
  useradd --system --home-dir /var/lib/iot-daq-gateway --create-home --shell /usr/sbin/nologin iot-daq
fi

install -d -o iot-daq -g iot-daq /var/lib/iot-daq-gateway
if [[ ! -f /var/lib/iot-daq-gateway/seed/gateway.yaml && -d /opt/iot-daq-gateway/data/seed ]]; then
  cp -a /opt/iot-daq-gateway/data/seed /var/lib/iot-daq-gateway/seed
  chown -R iot-daq:iot-daq /var/lib/iot-daq-gateway
fi

install -m 0644 "$ROOT/iot-daq-gateway.service" /etc/systemd/system/iot-daq-gateway.service
systemctl daemon-reload
systemctl enable --now iot-daq-gateway.service
systemctl --no-pager --full status iot-daq-gateway.service || true
