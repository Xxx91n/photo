#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="photoprivacy"
INSTALL_DIR="/opt/photoprivacy"
USER_NAME="photoprivacy"
GROUP_NAME="photoprivacy"
GUI_USER="${SUDO_USER:-}"
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
  --hot-folder <path>      Hot folder override passed to Worker
  --audit-folder <path>    Audit folder override passed to Worker
  --quarantine <path>      Quarantine folder for setup hints
  --user <name>            Service user (default: ${USER_NAME})
  --group <name>           Service group (default: ${GROUP_NAME})
  --gui-user <name>        GUI user to add to the service group for IPC socket access
                           (default: invoking sudo user, "${SUDO_USER:-}")
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
    --gui-user)
      GUI_USER="$2"
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

EXE_PATH="${INSTALL_DIR}/PhotoPrivacyWorker"
if [[ ! -x "$EXE_PATH" ]]; then
  echo "Missing executable: ${EXE_PATH}" >&2
  echo "Please extract release/linux-x64/worker/ package to ${INSTALL_DIR} first." >&2
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
chmod 2770 "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER"

# ADR 0067: service 模式 IPC socket 为 0660 + 组边界，GUI 用户必须在服务组内才能连上。
# 组边界不满足时 GUI 将无法访问 IPC（而非静默放行），故此处显式供给并报告。
if [[ -n "$GUI_USER" ]]; then
  if id -u "$GUI_USER" >/dev/null 2>&1; then
    usermod -aG "$GROUP_NAME" "$GUI_USER"
    echo "Added GUI user '${GUI_USER}' to group '${GROUP_NAME}' (IPC socket 0660 group boundary)."
  else
    echo "Warning: GUI user '${GUI_USER}' not found; skipped group supply." >&2
  fi
else
  echo "Warning: no GUI user resolved (--gui-user / SUDO_USER empty); service-mode UI will not reach the IPC socket." >&2
fi

SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"

# ADR 0021 (Q15b): Stop existing service before replacing the unit file.
# systemctl enable --now does not restart an already-running unit after daemon-reload,
# so we stop first to guarantee the new unit actually starts fresh.
if systemctl is-active --quiet "${SERVICE_NAME}" 2>/dev/null; then
    systemctl stop "${SERVICE_NAME}" || true
fi

cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=PhotoPrivacy Cleaner
After=network.target

[Service]
Type=simple
User=${USER_NAME}
Group=${GROUP_NAME}
WorkingDirectory=${INSTALL_DIR}
ExecStart=${EXE_PATH} --mode service --config ${CONFIG_PATH} --hot-folder ${HOT_FOLDER} --audit-folder ${AUDIT_FOLDER}
Restart=always
RestartSec=3
NoNewPrivileges=true
RuntimeDirectory=photoprivacy
RuntimeDirectoryMode=0750
ExecReload=/bin/kill -HUP $MAINPID

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
# First-time enable, then restart to pick up the new binary (covers both fresh install and upgrade).
systemctl enable "${SERVICE_NAME}" 2>/dev/null || true
systemctl restart "${SERVICE_NAME}"

echo "Installed and started ${SERVICE_NAME}.service"
echo "Check status: systemctl status ${SERVICE_NAME} --no-pager"
