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

# Create service group and user if needed.
# dscl -create 对已存在记录回 eDSRecordAlreadyExists（exit 55）：安装必须可重入，且 CI 虚拟环境
# （macOS runner，#32）实测出现 -read 守卫与 -create 结果不一致的形态——这里按幂等处理：
# 55 视为“已在位”继续，其余退出码照旧中止（不吞真错误）。
dscl_ensure() {
  local rc=0
  dscl . -create "$@" || rc=$?
  if [[ $rc -ne 0 && $rc -ne 55 ]]; then
    return "$rc"
  fi
}

# GID 不再钉死 400：标准 macOS 上 400 已被系统组 com.apple.access_remote_ae 占用，
# PrimaryGroupID 撞唯一性直接 eDSRecordAlreadyExists（#32 CI 实测）。组已存在取其现值，
# 否则在 400-499 取第一个空位；用户 PrimaryGroupID 跟组一致，否则 chown 用户:组 解析不了。
resolve_group_gid() {
  local gid
  gid="$(dscl . -read "/Groups/${GROUP_NAME}" PrimaryGroupID 2>/dev/null | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')"
  if [[ -n "$gid" ]]; then
    echo "$gid"
    return 0
  fi
  local candidate=400
  while [[ $candidate -lt 500 ]]; do
    if ! dscl . -list /Groups gid 2>/dev/null | awk -v g="$candidate" '$2 == g { found=1 } END { exit !found }'; then
      echo "$candidate"
      return 0
    fi
    candidate=$((candidate + 1))
  done
  return 1
}

GROUP_GID="$(resolve_group_gid)" || { echo "No free GID in 400-499" >&2; exit 1; }

# 用户侧同理：无 UniqueID 的记录对 getpwnam 不可见，chown 会报 illegal user name（#32 实测）。
resolve_user_uid() {
  local uid
  uid="$(dscl . -read "/Users/${USER_NAME}" UniqueID 2>/dev/null | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')"
  if [[ -n "$uid" ]]; then
    echo "$uid"
    return 0
  fi
  local candidate=400
  while [[ $candidate -lt 500 ]]; do
    if ! dscl . -list /Users uid 2>/dev/null | awk -v u="$candidate" '$2 == u { found=1 } END { exit !found }'; then
      echo "$candidate"
      return 0
    fi
    candidate=$((candidate + 1))
  done
  return 1
}

USER_UID="$(resolve_user_uid)" || { echo "No free UID in 400-499" >&2; exit 1; }

if ! dscl . -read /Groups/${GROUP_NAME} >/dev/null 2>&1; then
  dscl_ensure /Groups/${GROUP_NAME}
fi
dscl_ensure /Groups/${GROUP_NAME} PrimaryGroupID "$GROUP_GID"

if ! dscl . -read /Users/${USER_NAME} >/dev/null 2>&1; then
  dscl_ensure /Users/${USER_NAME}
fi
dscl_ensure /Users/${USER_NAME} UniqueID "$USER_UID"
dscl_ensure /Users/${USER_NAME} UserShell /usr/bin/false
dscl_ensure /Users/${USER_NAME} NFSHomeDirectory "${INSTALL_DIR}"
dscl_ensure /Users/${USER_NAME} PrimaryGroupID "$GROUP_GID"

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

# Unload existing label before loading the new plist, so the new binary starts fresh.
if launchctl list | grep -q "^\t*${LABEL}\b" 2>/dev/null; then
    launchctl unload -w "$PLIST_PATH" 2>/dev/null || true
fi

launchctl load -w "$PLIST_PATH"

echo "Installed and started ${LABEL}"
echo "Check status: launchctl list | grep ${LABEL}"
