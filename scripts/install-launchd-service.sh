#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="com.photoprivacy.cleaner"
LABEL="com.photoprivacy.cleaner"
INSTALL_DIR="/opt/photoprivacy"
USER_NAME="photoprivacy"
GROUP_NAME="photoprivacy"
HOT_FOLDER="/var/lib/photoprivacy/hot"
AUDIT_FOLDER="/var/log/photoprivacy"
QUARANTINE_FOLDER="/var/lib/photoprivacy/quarantine"
CONFIG_PATH="${INSTALL_DIR}/config/config.json"

usage() {
  cat <<EOF
Usage: sudo ./scripts/install-launchd-service.sh [options]

Options:
  --install-dir <path>   Install directory (default: ${INSTALL_DIR})
  --config <path>        Config file path (default: ${CONFIG_PATH})
  --hot-folder <path>    Hot folder (default: ${HOT_FOLDER})
  --audit-folder <path>  Audit folder (default: ${AUDIT_FOLDER})
  --quarantine <path>    Quarantine folder (default: ${QUARANTINE_FOLDER})
  --user <name>          Service user (default: ${USER_NAME})
  --group <name>         Service group (default: ${GROUP_NAME})
  -h, --help             Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    --config) CONFIG_PATH="$2"; shift 2 ;;
    --hot-folder) HOT_FOLDER="$2"; shift 2 ;;
    --audit-folder) AUDIT_FOLDER="$2"; shift 2 ;;
    --quarantine) QUARANTINE_FOLDER="$2"; shift 2 ;;
    --user) USER_NAME="$2"; shift 2 ;;
    --group) GROUP_NAME="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ $EUID -ne 0 ]]; then
  echo "Please run as root (sudo)." >&2
  exit 1
fi

EXE_PATH="${INSTALL_DIR}/PhotoPrivacyWorker"
if [[ ! -x "$EXE_PATH" ]]; then
  echo "Missing executable: ${EXE_PATH}" >&2
  echo "Please extract release/osx-arm64/worker/ package to ${INSTALL_DIR} first." >&2
  exit 1
fi

# Create service group and user if needed
if ! dscl . -read /Groups/${GROUP_NAME} >/dev/null 2>&1; then
  dscl . -create /Groups/${GROUP_NAME}
  dscl . -create /Groups/${GROUP_NAME} PrimaryGroupID 400
fi

if ! dscl . -read /Users/${USER_NAME} >/dev/null 2>&1; then
  dscl . -create /Users/${USER_NAME}
  dscl . -create /Users/${USER_NAME} UserShell /usr/bin/false
  dscl . -create /Users/${USER_NAME} NFSHomeDirectory "${INSTALL_DIR}"
  dscl . -create /Users/${USER_NAME} PrimaryGroupID 400
fi

mkdir -p "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER"
chown -R "${USER_NAME}:${GROUP_NAME}" "$INSTALL_DIR" "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER"
chmod 2770 "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER"

PLIST_PATH="/Library/LaunchDaemons/${LABEL}.plist"
cat > "$PLIST_PATH" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>${LABEL}</string>
    <key>ProgramArguments</key>
    <array>
        <string>${EXE_PATH}</string>
        <string>--mode</string>
        <string>service</string>
        <string>--config</string>
        <string>${CONFIG_PATH}</string>
        <string>--hot-folder</string>
        <string>${HOT_FOLDER}</string>
        <string>--audit-folder</string>
        <string>${AUDIT_FOLDER}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <dict>
        <key>SuccessfulExit</key>
        <false/>
    </dict>
    <key>UserName</key>
    <string>${USER_NAME}</string>
    <key>GroupName</key>
    <string>${GROUP_NAME}</string>
    <key>WorkingDirectory</key>
    <string>${INSTALL_DIR}</string>
    <key>StandardOutPath</key>
    <string>${AUDIT_FOLDER}/stdout.log</string>
    <key>StandardErrorPath</key>
    <string>${AUDIT_FOLDER}/stderr.log</string>
</dict>
</plist>
EOF

chmod 644 "$PLIST_PATH"
chown root:wheel "$PLIST_PATH"

launchctl load -w "$PLIST_PATH"

echo "Installed and started ${LABEL}"
echo "Check status: launchctl list | grep ${LABEL}"
