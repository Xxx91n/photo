#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="photoprivacy"
INSTALL_DIR="/opt/photoprivacy"
USER_NAME="photoprivacy"
GROUP_NAME="photoprivacy"
HOT_FOLDER="/var/lib/photoprivacy/hot"
AUDIT_FOLDER="/var/log/photoprivacy"
QUARANTINE_FOLDER="/var/lib/photoprivacy/quarantine"
CONFIG_PATH="${INSTALL_DIR}/config/config.json"

usage() {
  cat <<EOF
Usage: sudo ./scripts/install-systemd-service.sh [options]

Options:
  --install-dir <path>     Install directory (default: ${INSTALL_DIR})
  --config <path>          Config file path (default: ${CONFIG_PATH})
  --hot-folder <path>      Hot folder override passed to CLI
  --audit-folder <path>    Audit folder override passed to CLI
  --quarantine <path>      Quarantine folder for setup hints
  --user <name>            Service user (default: ${USER_NAME})
  --group <name>           Service group (default: ${GROUP_NAME})
  -h, --help               Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --install-dir)
      INSTALL_DIR="$2"
      shift 2
      ;;
    --config)
      CONFIG_PATH="$2"
      shift 2
      ;;
    --hot-folder)
      HOT_FOLDER="$2"
      shift 2
      ;;
    --audit-folder)
      AUDIT_FOLDER="$2"
      shift 2
      ;;
    --quarantine)
      QUARANTINE_FOLDER="$2"
      shift 2
      ;;
    --user)
      USER_NAME="$2"
      shift 2
      ;;
    --group)
      GROUP_NAME="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ $EUID -ne 0 ]]; then
  echo "Please run as root (sudo)." >&2
  exit 1
fi

EXE_PATH="${INSTALL_DIR}/PhotoPrivacy"
if [[ ! -x "$EXE_PATH" ]]; then
  echo "Missing executable: ${EXE_PATH}" >&2
  echo "Please extract linux-x64 package to ${INSTALL_DIR} first." >&2
  exit 1
fi

if ! getent group "$GROUP_NAME" >/dev/null; then
  groupadd --system "$GROUP_NAME"
fi

if ! id -u "$USER_NAME" >/dev/null 2>&1; then
  useradd --system --home-dir "$INSTALL_DIR" --shell /usr/sbin/nologin --gid "$GROUP_NAME" "$USER_NAME"
fi

mkdir -p "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER"
chown -R "$USER_NAME:$GROUP_NAME" "$INSTALL_DIR" "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER"

SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=PhotoPrivacy Cleaner
After=network.target

[Service]
Type=simple
User=${USER_NAME}
Group=${GROUP_NAME}
WorkingDirectory=${INSTALL_DIR}
ExecStart=${EXE_PATH} --mode cli --config ${CONFIG_PATH} --hot-folder ${HOT_FOLDER} --audit-folder ${AUDIT_FOLDER}
Restart=always
RestartSec=3
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now "${SERVICE_NAME}"

echo "Installed and started ${SERVICE_NAME}.service"
echo "Check status: systemctl status ${SERVICE_NAME} --no-pager"
