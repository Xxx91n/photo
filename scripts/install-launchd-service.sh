#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="com.photoprivacy.cleaner"
LABEL="com.photoprivacy.cleaner"
INSTALL_DIR="/opt/photoprivacy"
USER_NAME="photoprivacy"
GROUP_NAME="photoprivacy"
# socket 访问组与数据组分离（#167 评审 CWE-732）：hot/quarantine 以 2770 归数据组，若把 GUI
# 用户加进数据组，它就能绕过 IPC 直接删审计与隔离文件。socket 单独一组，只有该组成员能连 0660 的
# worker.sock；服务用户同属两组，数据目录写入靠「属主」身份。审计目录例外（组换 socket 组、2750/0640），
# 见下方 chown/chmod 段。
SOCKET_GROUP="photoprivacy-ipc"
HOT_FOLDER="/var/lib/photoprivacy/hot"
AUDIT_FOLDER="/var/log/photoprivacy"
QUARANTINE_FOLDER="/var/lib/photoprivacy/quarantine"
CONFIG_PATH="${INSTALL_DIR}/config/config.json"
# 服务模式 UDS 的系统级目录（WorkerIpcEndpointNames.MacSystemSocketDirectory）：与 Linux
# RuntimeDirectory（0750）同构——root 建、属主为服务用户、socket 访问组可遍历；GUI 用户
# 加入 socket 访问组后才能穿过目录并连上 0660 的 socket（#167）。
SERVICE_SOCKET_DIR="/Library/Application Support/photoprivacy"
# 服务模式 IPC 的调用者：默认取 sudo 的原始用户，显式可用 --gui-user 指定（与 Linux 同一套规则）。
GUI_USER="${SUDO_USER:-}"
# 显式与隐式两种来源的可失败性不同：`--gui-user` 是调用方的明确意图，用户不存在或加不进
# socket 组时安装必须以非零退出失败；`SUDO_USER` 只是环境推断，可能为空或不适用（自动化
# 安装、root 直登），只告警继续，避免把推断噪声升级成安装失败（评审 Major）。
GUI_USER_EXPLICIT=0

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
  --group <name>         Service group, owns data dirs hot/audit/quarantine (default: ${GROUP_NAME})
  --socket-group <name>  IPC socket access group, distinct from the data group and granted to the
                         GUI user (default: ${SOCKET_GROUP}); must be non-empty and differ from --group
  --gui-user <name>      GUI user to add to the socket access group
                         (default: SUDO_USER; explicit value fails the install when the
                         account is missing or cannot be added to the group)
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
    --socket-group) SOCKET_GROUP="$2"; shift 2 ;;
    --gui-user)
      # 显式空值必须直接报错：早前 `--gui-user ""` 只把 GUI_USER 置空、GUI_USER_EXPLICIT=1，随后落进
      # 「未解析到 GUI 用户」的告警分支以 exit 0 收场，调用方会把「没给名字」误判成「IPC 权限已供给」
      # （#180 评审 R5）。真缺值（$2 未定义）同样在此拒绝，早于任何账户/目录副作用。
      if [[ -z "${2//[[:space:]]/}" ]]; then
        echo "Error: --gui-user requires a non-empty user name; omit the flag to fall back to SUDO_USER." >&2
        exit 1
      fi
      GUI_USER="$2"; GUI_USER_EXPLICIT=1; shift 2 ;;
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
# （macOS runner，#32）实测出现 -read 检查与 -create 结果不一致的形态——这里按幂等处理：
# 55 视为“已在位”继续，其余退出码照旧中止（不吞真错误）。
dscl_ensure() {
  local rc=0
  dscl . -create "$@" || rc=$?
  if [[ $rc -ne 0 && $rc -ne 55 ]]; then
    return "$rc"
  fi
}

# dscl -create 组记录与写 PrimaryGroupID 是两步语义：id 写失败记录仍在，于是 chown 用户:组
# 报 illegal group name，重装又会撞上「已存在但无 GID」直接中止（#180 CI 实测）。新建组落盘后
# 必须回读确认，失败就删掉半成品记录再中止，保持安装可重入。
assert_group_gid() {
  local name="$1" expect="$2" actual
  actual="$(dscl . -read "/Groups/${name}" PrimaryGroupID 2>/dev/null | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')"
  if [[ "$actual" == "$expect" ]]; then
    return 0
  fi
  echo "Failed to assign GID ${expect} to group ${name} (actual: ${actual:-<none>}); removing incomplete record" >&2
  dscl . -delete "/Groups/${name}" >/dev/null 2>&1 || true
  exit 1
}

# 用户记录同理：建用户是 UniqueID / shell / home / PrimaryGroupID 多步写，set -e 中途失败会留下
# 「已存在但读不到 UniqueID」的半成品——getpwnam 看不见它，chown 报 illegal user name，下次安装
# 又走「既有账户」分支直接中止，装不回去（组侧同缺陷已在 #180 修，用户侧此前漏改，评审 R4）。
# 任一步失败或回读不到 UID，删掉半成品记录再中止，保持安装可重入。
ensure_user_with_uid() {
  local name="$1" uid="$2" gid="$3" actual
  if ! { dscl_ensure "/Users/${name}" \
      && dscl_ensure "/Users/${name}" UniqueID "$uid" \
      && dscl_ensure "/Users/${name}" UserShell /usr/bin/false \
      && dscl_ensure "/Users/${name}" NFSHomeDirectory "${INSTALL_DIR}" \
      && dscl_ensure "/Users/${name}" PrimaryGroupID "$gid"; }; then
    echo "Failed to create user ${name} (uid ${uid}); removing incomplete record" >&2
    dscl . -delete "/Users/${name}" >/dev/null 2>&1 || true
    exit 1
  fi
  actual="$(dscl . -read "/Users/${name}" UniqueID 2>/dev/null | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')"
  if [[ "$actual" != "$uid" ]]; then
    echo "Failed to assign UID ${uid} to user ${name} (actual: ${actual:-<none>}); removing incomplete record" >&2
    dscl . -delete "/Users/${name}" >/dev/null 2>&1 || true
    exit 1
  fi
}

# GID 不再钉死 400：标准 macOS 上 400 已被系统组 com.apple.access_remote_ae 占用，
# PrimaryGroupID 撞唯一性直接 eDSRecordAlreadyExists（#32 CI 实测）。组已存在取其现值，
# 否则在 400-499 取第一个空位；用户 PrimaryGroupID 跟组一致，否则 chown 用户:组 解析不了。
# 目标组名必须显式传入：早期版本读全局 GROUP_NAME，建 socket 访问组时会复用数据组 GID，
# 撞唯一性后 socket 组拿不到 GID，审计目录 chown 直接 illegal group name（#180 CI 实测）。
# 建组 + 写 GID 一步到位：PrimaryGroupID 写失败时 dscl 会留下「无 GID 的组记录」，下次安装会
# 撞上 eDSRecordAlreadyExists 且无法自愈（#167 第三轮评审）。失败先删残缺记录再退出。
ensure_group_with_gid() {
  local name="$1" gid="$2"
  dscl_ensure "/Groups/${name}"
  if ! dscl_ensure "/Groups/${name}" PrimaryGroupID "${gid}"; then
    echo "Failed to write PrimaryGroupID ${gid} for group ${name}; removing incomplete record" >&2
    dscl . -delete "/Groups/${name}" >/dev/null 2>&1 || true
    exit 1
  fi
  assert_group_gid "${name}" "${gid}"
}
resolve_group_gid() {
  local name="$1"
  local gid
  gid="$(dscl . -read "/Groups/${name}" PrimaryGroupID 2>/dev/null | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')"
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

# 既有账户一律不覆写属性（dscl -create 会替换既有值；重装写穿他人 shell/home/GID 属数据完整性
# 事故，CodeRabbit 审查意见）——已存在的组/用户必须已有可解析 GID/UID，缺了就中止交人工处置，
# 只有新建账户才写属性。GID/UID 选空位逻辑见 resolve_*（400 起步避让系统占用，#32）。
GROUP_EXISTED=0
if dscl . -read "/Groups/${GROUP_NAME}" >/dev/null 2>&1; then
  GROUP_EXISTED=1
fi
USER_EXISTED=0
if dscl . -read "/Users/${USER_NAME}" >/dev/null 2>&1; then
  USER_EXISTED=1
fi

if [[ "$GROUP_EXISTED" -eq 1 ]]; then
  GROUP_GID="$(dscl . -read "/Groups/${GROUP_NAME}" PrimaryGroupID 2>/dev/null | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')"
  if [[ -z "$GROUP_GID" ]]; then
    echo "Existing group ${GROUP_NAME} has no readable PrimaryGroupID; 请人工处置该账户后重试" >&2
    exit 1
  fi
else
  GROUP_GID="$(resolve_group_gid "$GROUP_NAME")" || { echo "No free GID in 400-499" >&2; exit 1; }
  ensure_group_with_gid "$GROUP_NAME" "$GROUP_GID"
fi

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

if [[ "$USER_EXISTED" -eq 1 ]]; then
  USER_UID="$(dscl . -read "/Users/${USER_NAME}" UniqueID 2>/dev/null | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')"
  if [[ -z "$USER_UID" ]]; then
    echo "Existing user ${USER_NAME} has no readable UniqueID; 请人工处置该账户后重试" >&2
    exit 1
  fi
else
  USER_UID="$(resolve_user_uid)" || { echo "No free UID in 400-499" >&2; exit 1; }
  ensure_user_with_uid "$USER_NAME" "$USER_UID" "$GROUP_GID"
fi

# socket 访问组必须独立于数据组：数据目录 2770 归数据组，同组成员可绕过 IPC 直接删审计与隔离
# 文件（#167 评审 CWE-732）。同名或空值一律拒绝，避免静默退化成「GUI 用户拥有数据写权限」。
if [[ -z "$SOCKET_GROUP" ]]; then
  echo "Error: --socket-group must not be empty (IPC socket access group)." >&2
  exit 1
fi
if [[ "$SOCKET_GROUP" == "$GROUP_NAME" ]]; then
  echo "Error: --socket-group must differ from --group '${GROUP_NAME}';" >&2
  echo "       granting GUI users the data group would let them delete audit/quarantine files directly." >&2
  exit 1
fi

if dscl . -read "/Groups/${SOCKET_GROUP}" >/dev/null 2>&1; then
  SOCKET_GID="$(dscl . -read "/Groups/${SOCKET_GROUP}" PrimaryGroupID 2>/dev/null | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')"
  if [[ -z "$SOCKET_GID" ]]; then
    echo "Existing group ${SOCKET_GROUP} has no readable PrimaryGroupID; 请人工处置该组后重试" >&2
    exit 1
  fi
  # 名字不同但 GID 相同等于同一组权限：GUI 用户进了 socket 组就继承数据组对 hot/quarantine 的
  # 2770 访问，组隔离形同虚设（评审 CWE-732 补充）。
  if [[ "$SOCKET_GID" == "$GROUP_GID" ]]; then
    echo "Error: existing group ${SOCKET_GROUP} reuses data-group GID ${GROUP_GID};" >&2
    echo "       use a distinct socket group so GUI access cannot reach data directories." >&2
    exit 1
  fi
else
  SOCKET_GID="$(resolve_group_gid "$SOCKET_GROUP")" || { echo "No free GID in 400-499 for ${SOCKET_GROUP}" >&2; exit 1; }
  ensure_group_with_gid "$SOCKET_GROUP" "$SOCKET_GID"
fi

# 服务用户必须是 socket 组成员：plist 以 GroupName=SOCKET_GROUP 起 Worker，非成员会被 launchd 拒绝；
# 数据目录写入仍靠「属主」身份，不依赖该组。
if ! dseditgroup -o checkmember -m "$USER_NAME" "$SOCKET_GROUP" >/dev/null 2>&1; then
  if ! dseditgroup -o edit -a "$USER_NAME" -t user "$SOCKET_GROUP" >/dev/null 2>&1; then
    echo "Failed to add service user '${USER_NAME}' to socket group '${SOCKET_GROUP}'" >&2
    exit 1
  fi
fi

mkdir -p "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER" "${INSTALL_DIR}/logs"
chown -R "${USER_NAME}:${GROUP_NAME}" "$INSTALL_DIR" "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER"
# 审计目录紧接着改成 2750 + socket 组，这里不再先给 2770 再收回（评审 nit：同一目录先放宽再收紧，
# 中间存在组写窗口，也让权限意图读起来自相矛盾）。
chmod 2770 "$HOT_FOLDER" "$QUARANTINE_FOLDER"

# 安装目录与 Worker 日志目录此前完全没设权限：发布包按 umask 解压成 0755，本地任意用户能遍历读走
# config 与 logs/worker-*.log（请求 URL、文件名、错误栈，评审 R1）。
# 但安装目录不能一刀切 0750：GUI 用户要从这里执行 UI 二进制，实测 0750 下
# `sudo: unable to execute /opt/photoprivacy/PhotoPrivacy: Permission denied`，UI 直接起不来。
# 故安装目录收到 0755（属主服务用户，其他人只读可执行、不可写），真正敏感的面单独收紧：
# config/ 与 logs/（目录 0750/2750 组可进不可写、文件 0640 组只读），其他用户连目录都进不去。
mkdir -p "${INSTALL_DIR}/logs"
chmod 0755 "$INSTALL_DIR"
chmod 2750 "${INSTALL_DIR}/logs"
find "${INSTALL_DIR}/logs" -type f -exec chmod 0640 {} +
if [[ -d "${INSTALL_DIR}/config" ]]; then
  chmod 0750 "${INSTALL_DIR}/config"
  find "${INSTALL_DIR}/config" -type f -exec chmod 0640 {} +
fi

# 审计目录例外：UI 日志页以本地 tail 为主通道（photoprivacy-logs），GUI 用户必须能读；
# 评审要求「先移除受保护目录的组写权限，再给 GUI 用户访问」——所以组换成 socket 访问组，
# 目录 2750（组只有 r-x，删不掉），文件 0640（组只读，改不了），属主仍是服务用户（Worker 继续落盘）。
# hot / quarantine 保持数据组 2770：GUI 用户不进数据组，就完全不可见。
chown -R "${USER_NAME}:${SOCKET_GROUP}" "$AUDIT_FOLDER"
find "$AUDIT_FOLDER" -type d -exec chmod 2750 {} +
find "$AUDIT_FOLDER" -type f -exec chmod 0640 {} +

# 服务模式 UDS 目录：0750 + 服务组，与 Linux RuntimeDirectory 同构（#167）。
# 不用 /var/run：macOS 上重启即清，非 root 的 Worker 无权重建目录。
mkdir -p "$SERVICE_SOCKET_DIR"
chown "${USER_NAME}:${SOCKET_GROUP}" "$SERVICE_SOCKET_DIR"
chmod 0750 "$SERVICE_SOCKET_DIR"

# 组边界供给：socket 为 0660 + SOCKET_GROUP，GUI 用户不入该组就连不上（#167）。
# 只加 socket 组、不加数据组——否则 GUI 用户拿到 hot/audit/quarantine 的组写权限，
# 可绕过 IPC 直接删审计与隔离文件（评审 CWE-732）。
# 与 Linux 的 usermod -aG 同义；组变更对已运行进程不生效，需该用户重新登录。
if [[ -n "$GUI_USER" ]]; then
  if ! dscl . -read "/Users/${GUI_USER}" UniqueID >/dev/null 2>&1; then
    if [[ "$GUI_USER_EXPLICIT" -eq 1 ]]; then
      echo "Error: GUI user '${GUI_USER}' given by --gui-user does not exist." >&2
      exit 1
    fi
    echo "Warning: GUI user '${GUI_USER}' not found; skipped group supply." >&2
  elif dseditgroup -o checkmember -m "$GUI_USER" "$SOCKET_GROUP" >/dev/null 2>&1; then
    echo "GUI user '${GUI_USER}' already in group '${SOCKET_GROUP}'."
  elif dseditgroup -o edit -a "$GUI_USER" -t user "$SOCKET_GROUP" >/dev/null 2>&1; then
    echo "Added GUI user '${GUI_USER}' to group '${SOCKET_GROUP}' (IPC socket 0660 group boundary)."
    echo "Note: re-login required for the new group membership to reach existing sessions."
  else
    if [[ "$GUI_USER_EXPLICIT" -eq 1 ]]; then
      echo "Error: failed to add '${GUI_USER}' from --gui-user to '${SOCKET_GROUP}'; service-mode UI cannot reach the IPC socket." >&2
      exit 1
    fi
    echo "Warning: failed to add '${GUI_USER}' to '${SOCKET_GROUP}'; service-mode UI cannot reach the IPC socket." >&2
  fi
  # 若历史安装已把该用户加进数据组，提示但不自动移除（撤销他人既有授权需人工确认）。
  if dseditgroup -o checkmember -m "$GUI_USER" "$GROUP_NAME" >/dev/null 2>&1; then
    echo "Warning: '${GUI_USER}' is also a member of data group '${GROUP_NAME}' (2770 on hot/quarantine);" >&2
    echo "         that grants direct file deletion bypassing IPC. Remove it with:" >&2
    echo "         sudo dseditgroup -o edit -d ${GUI_USER} -t user ${GROUP_NAME}" >&2
  fi
else
  echo "Warning: no GUI user resolved (--gui-user / SUDO_USER empty); service-mode UI will not reach the IPC socket." >&2
fi

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
    <string>${SOCKET_GROUP}</string>
    <key>Umask</key>
    <integer>23</integer>
    <key>EnvironmentVariables</key>
    <dict>
        <key>PHOTOPRIVACY_SERVICE_GROUP</key>
        <string>${SOCKET_GROUP}</string>
    </dict>
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
