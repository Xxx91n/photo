#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="photoprivacy"
INSTALL_DIR="/opt/photoprivacy"
USER_NAME="photoprivacy"
GROUP_NAME="photoprivacy"
# socket 访问组与数据组分离（#167 评审 CWE-732，Linux 同款 #188）：hot/quarantine 以 2770 归数据组，
# 若把 GUI 用户加进数据组，它就能绕过 IPC 直接删审计与隔离文件。socket 单独一组，只有该组成员能连
# 0660 的 worker.sock；unit Group= 指向该组，Worker 主 GID 决定 socket 与 RuntimeDirectory 的属组，
# 数据目录写入靠「属主」身份。审计目录例外（组换 socket 组、2750/0640），见下方 chown/chmod 段。
SOCKET_GROUP="photoprivacy-ipc"
GUI_USER="${SUDO_USER:-}"
# 显式与隐式两种来源的可失败性不同（macOS R5 同款）：`--gui-user` 是调用方的明确意图，用户不存在
# 或加不进 socket 组时安装必须以非零退出失败；`SUDO_USER` 只是环境推断，可能为空或不适用（自动化
# 安装、root 直登），只告警继续，避免把推断噪声升级成安装失败。
GUI_USER_EXPLICIT=0
HOT_FOLDER="/var/lib/photoprivacy/hot"
AUDIT_FOLDER="/var/log/photoprivacy"
QUARANTINE_FOLDER="/var/lib/photoprivacy/quarantine"
CONFIG_PATH="${INSTALL_DIR}/config/config.json"

# usage：打印命令行帮助（选项、默认值与 --gui-user/--socket-group 的失败语义）。
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
  --group <name>           Service (data) group, owns hot/quarantine and install-dir files
                           (default: ${GROUP_NAME})
  --socket-group <name>    IPC socket access group, distinct from the data group and granted to the
                           GUI user (default: ${SOCKET_GROUP}); must be non-empty and differ from --group
  --gui-user <name>        GUI user to add to the socket access group
                           (default: SUDO_USER; explicit value fails the install when the
                           account is missing or cannot be added to the group)
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
    --socket-group)
      SOCKET_GROUP="$2"
      shift 2
      ;;
    --gui-user)
      # 显式空值必须硬失败（macOS #180 评审 R5 同款）：否则 `--gui-user ""` 只把 GUI_USER 置空，
      # 随后落进「未解析到 GUI 用户」的告警分支以 exit 0 收场，调用方会把「没给名字」误判成
      # 「IPC 权限已供给」。真缺值（$2 未定义）同样在此拒绝，早于任何账户/目录副作用。
      if [[ -z "${2//[[:space:]]/}" ]]; then
        echo "Error: --gui-user requires a non-empty user name; omit the flag to fall back to SUDO_USER." >&2
        exit 1
      fi
      GUI_USER="$2"; GUI_USER_EXPLICIT=1; shift 2 ;;
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

# socket 访问组必须独立于数据组：数据目录 2770 归数据组，同组成员可绕过 IPC 直接删审计与隔离
# 文件（#167 评审 CWE-732）。同名或空值一律在参数解析阶段拒绝，早于任何账户/目录副作用。
if [[ -z "$SOCKET_GROUP" ]]; then
  echo "Error: --socket-group must not be empty (IPC socket access group)." >&2
  exit 1
fi
if [[ "$SOCKET_GROUP" == "$GROUP_NAME" ]]; then
  echo "Error: --socket-group must differ from --group '${GROUP_NAME}';" >&2
  echo "       granting GUI users the data group would let them delete audit/quarantine files directly." >&2
  exit 1
fi

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
if ! getent group "$SOCKET_GROUP" >/dev/null; then
  groupadd --system "$SOCKET_GROUP"
fi

# 名字不同但 GID 相同等于同一组权限：GUI 用户进了 socket 组就继承数据组对 hot/quarantine 的
# 2770 访问，组隔离形同虚设（macOS #180 评审 CWE-732 补充，Linux 同款）。既有组复用 GID 时
# 中止交人工处置，不自动改他人组的 GID。
GROUP_GID="$(getent group "$GROUP_NAME" | cut -d: -f3)"
SOCKET_GID="$(getent group "$SOCKET_GROUP" | cut -d: -f3)"
if [[ -z "$GROUP_GID" || -z "$SOCKET_GID" ]]; then
  echo "Error: cannot resolve GID for '${GROUP_NAME}' / '${SOCKET_GROUP}'; 请人工处置该账户后重试" >&2
  exit 1
fi
if [[ "$SOCKET_GID" == "$GROUP_GID" ]]; then
  echo "Error: existing group ${SOCKET_GROUP} reuses data-group GID ${GROUP_GID};" >&2
  echo "       use a distinct socket group so GUI access cannot reach data directories." >&2
  exit 1
fi

if ! id -u "$USER_NAME" >/dev/null 2>&1; then
  useradd --system --home-dir "$INSTALL_DIR" --shell /usr/sbin/nologin --gid "$GROUP_NAME" "$USER_NAME"
fi

# 服务用户进 socket 组：systemd 按 Group= 直接设主 GID、不查成员资格，这里保持与 macOS 一致的
# 账户模型（launchd 要求成员资格），手工以服务用户起 Worker 时也带 socket 组身份。
usermod -aG "$SOCKET_GROUP" "$USER_NAME"

mkdir -p "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER" "${INSTALL_DIR}/logs"
chown -R "$USER_NAME:$GROUP_NAME" "$INSTALL_DIR" "$HOT_FOLDER" "$AUDIT_FOLDER" "$QUARANTINE_FOLDER"
# hot/quarantine 保持数据组 2770；审计目录紧接着换 socket 组并收成 2750，不再先给 2770 再收回
# （macOS 评审 nit：同一目录先放宽再收紧，中间存在组写窗口，也让权限意图读起来自相矛盾）。
chmod 2770 "$HOT_FOLDER" "$QUARANTINE_FOLDER"

# 安装目录与 Worker 日志目录此前完全没设权限（#188，macOS R1 同缺陷）：发布包按 umask 解压成
# 0755，本地任意用户能遍历读走 config 与 logs/worker-*.log（请求 URL、文件名、错误栈）。
# 但安装目录不能一刀切 0750：GUI 用户要从这里执行 UI 二进制，macOS 实测 0750 下
# `sudo: unable to execute /opt/photoprivacy/PhotoPrivacy: Permission denied`，UI 直接起不来。
# 故安装目录收到 0755（属主服务用户，其他人只读可执行、不可写），真正敏感的面单独收紧：
# config/ 与 logs/（目录 0750/2750 组可进不可写、文件 0640 组只读），其他用户连目录都进不去。
# ACL 剥离（#188 根因，先于 chmod）：chmod 只改基础 mode，发布包从 CI workspace 继承的
# 命名 ACL（实证 config.json 带 user:runner:rwx #effective:r--）会绕过 0640，让非数据组的
# GUI 用户读穿 config 与 worker 日志；目录 default ACL 还会让运行期新日志继续继承。
# setfacl -b 清空扩展 ACL（含 default），使 mode 成为唯一访问控制。必须先剥再 chmod：
# -b 保留 group:: 基础条目（可能 rwx），随后 chmod 才能把最终 mode 收到 0640/0750/2750。
# acl 工具缺失则跳过（生产发布包不带 CI ACL；CI runner 必有 acl 包，getfacl 已实证在位）。
if command -v setfacl >/dev/null 2>&1; then
  find "$INSTALL_DIR" -exec setfacl -b {} + 2>/dev/null || true
fi
chmod 0755 "$INSTALL_DIR"
chmod 2750 "${INSTALL_DIR}/logs"
find "${INSTALL_DIR}/logs" -type f -exec chmod 0640 {} +
if [[ -d "${INSTALL_DIR}/config" ]]; then
  chmod 0750 "${INSTALL_DIR}/config"
  find "${INSTALL_DIR}/config" -type f -exec chmod 0640 {} +
fi

# 审计目录例外：本地 tail（photoprivacy-logs）与取证脚本以 GUI 用户身份直接读日志，
# 组换成 socket 访问组，目录 2750（组只有 r-x，删不掉），文件 0640（组只读，改不了），
# 属主仍是服务用户（Worker 继续落盘）。hot / quarantine 保持数据组 2770：GUI 用户不进数据组，
# 就完全不可见。
chown -R "$USER_NAME:$SOCKET_GROUP" "$AUDIT_FOLDER"
find "$AUDIT_FOLDER" -type d -exec chmod 2750 {} +
find "$AUDIT_FOLDER" -type f -exec chmod 0640 {} +

# 组边界供给：socket 为 0660 + SOCKET_GROUP，GUI 用户不入该组就连不上（#167/#188）。
# 只加 socket 组、不加数据组——否则 GUI 用户拿到 hot/quarantine 的组写权限，
# 可绕过 IPC 直接删审计与隔离文件（评审 CWE-732）。
# 组变更对已运行进程不生效，需该用户重新登录。
if [[ -n "$GUI_USER" ]]; then
  if ! id -u "$GUI_USER" >/dev/null 2>&1; then
    if [[ "$GUI_USER_EXPLICIT" -eq 1 ]]; then
      echo "Error: GUI user '${GUI_USER}' given by --gui-user does not exist." >&2
      exit 1
    fi
    echo "Warning: GUI user '${GUI_USER}' not found; skipped group supply." >&2
  elif usermod -aG "$SOCKET_GROUP" "$GUI_USER"; then
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
  if id -nG "$GUI_USER" 2>/dev/null | tr ' ' '\n' | grep -qx "$GROUP_NAME"; then
    echo "Warning: '${GUI_USER}' is also a member of data group '${GROUP_NAME}' (2770 on hot/quarantine);" >&2
    echo "         that grants direct file deletion bypassing IPC. Remove it with:" >&2
    echo "         sudo gpasswd -d ${GUI_USER} ${GROUP_NAME}" >&2
  fi
else
  echo "Warning: no GUI user resolved (--gui-user / SUDO_USER empty); service-mode UI will not reach the IPC socket." >&2
fi

SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"

# Stop existing service before replacing the unit file.
# systemctl enable --now does not restart an already-running unit after daemon-reload,
# so we stop first to guarantee the new unit actually starts fresh.
if systemctl is-active --quiet "${SERVICE_NAME}" 2>/dev/null; then
    systemctl stop "${SERVICE_NAME}"
fi

cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=PhotoPrivacy Cleaner
After=network.target

[Service]
Type=simple
User=${USER_NAME}
# Group 指向 socket 访问组（#188，macOS plist GroupName 同款）：Worker 主 GID 决定
# worker.sock（0660）与 RuntimeDirectory（0750）的属组，GUI 用户进该组即可连端点；
# hot/quarantine/audit 的写入靠「属主」身份（服务用户），不依赖数据组成员资格。
Group=${SOCKET_GROUP}
# 服务端对端白名单跟着 unit 的 Group 走（#167）：GUI 用户进的就是这个组，
# 不注入的话 Worker 会拿硬编码默认名比对，自定义 --socket-group 的机器上合法调用者被拒。
Environment=PHOTOPRIVACY_SERVICE_GROUP=${SOCKET_GROUP}
# 运行期新文件 0640 / 新目录 0750（macOS plist Umask 23 同款）：Worker Serilog 与审计的
# 运行期新文件也要落在组只读口径，安装期 chmod 只覆盖既有文件。
UMask=0027
WorkingDirectory=${INSTALL_DIR}
# umask 0027 经 sh 兜底再 exec：UMask= 已写入 unit，但 CI Ubuntu runner 实证 Worker 进程
# umask 仍为 000（Serilog 日志落 0666 而非 0640）——服务管理器未把 UMask 施加到进程时，
# 由启动壳显式 umask 保证运行期新文件 0640/新目录 0750，exec 替换后 MainPID 仍是 Worker。
ExecStart=/bin/sh -c 'umask 0027; exec "${EXE_PATH}" --mode service --config "${CONFIG_PATH}" --hot-folder "${HOT_FOLDER}" --audit-folder "${AUDIT_FOLDER}"'
Restart=always
RestartSec=3
NoNewPrivileges=true
RuntimeDirectory=photoprivacy
RuntimeDirectoryMode=0750
ExecReload=/bin/kill -HUP \$MAINPID

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
# First-time enable, then restart to pick up the new binary (covers both fresh install and upgrade).
# set -e makes enable/restart failures abort the install instead of reporting a disabled service as success.
systemctl enable "${SERVICE_NAME}" 2>/dev/null
systemctl restart "${SERVICE_NAME}"

echo "Installed and started ${SERVICE_NAME}.service"
echo "Check status: systemctl status ${SERVICE_NAME} --no-pager"
