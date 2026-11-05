#!/usr/bin/env bash
# verify-systemd-service.sh —— #188 Linux systemd 安装链权限/生命周期验证 harness。
#
# 用途：在 Linux（实机或 CI Ubuntu runner 虚拟环境，systemd 为 PID 1）对 systemd 服务安装链做
# 端到端断言：安装 → 注册 → 常驻（Restart=always 重启）→ 日志 → socket/权限组边界 → 停止 →
# 启动 → 卸载 → 清理。结构对齐 scripts/verify-launchd-service.sh（#32 macOS 同款 harness）。
# 操作与判定口径对齐 ui/crates/photoprivacy-service/src/unix.rs：
#   - 安装走 scripts/install-systemd-service.sh（LinuxOps::install 同款，仅跳过 pkexec 提权包装——
#     本 harness 本身要求 root 运行）；
#   - 停止/启动/卸载用 LinuxOps 同款 systemctl 命令串；
#   - 状态判定同 LinuxProbe::state + parse_systemctl_active：systemctl is-active 退出码
#     0=Running / 3=Stopped / 4=NotInstalled，其余按 stdout 文本。
# 因此脚本结论可直接代表新 UI Service 页探测/操作语义；UI 层机器可读证据：
#   PhotoPrivacy --service-probe-report --service-probe-ipc 以 GUI 用户语境运行，与 CLI 判定对拍
#   （硬断言；探针建窗前退出，无头 runner 可跑，不需要图形会话）。
# 权限断言（#188 核心）：逐项对拍 mode/group，不只看目录在不在——
#   安装目录 0755、config/ 0750+0640、logs/ 2750+0640（数据组）；审计目录 socket 组 2750+0640；
#   hot/quarantine 数据组 2770；socket 0660 socket 组、目录 0750；unit Group/Environment/UMask 注入；
#   GUI 用户只进 socket 组（可读审计、不可写删审计、不可读 config/worker 日志、不可进 hot/quarantine、
#   可执行 UI 二进制）；服务用户对数据目录仍可写（收敛不打断数据路径）。
# 用法：sudo ./scripts/verify-systemd-service.sh --release-dir <dir> [--evidence-dir <dir>]
#   --release-dir   publish.sh 产出目录（须含 PhotoPrivacyWorker 与 config/config.json）
#   --evidence-dir  证据落盘目录（默认 logs/systemd-verify；CI 传 $RUNNER_TEMP 下路径）
#
# 注意：harness 会把发布包重新铺进 INSTALL_DIR 并整链安装/卸载，只应在实机验证或一次性
# 虚拟环境（CI Ubuntu runner）里跑；卸载语义与产品一致（stop + disable + 删 unit + daemon-reload），
# 不删服务用户与目录。
set -euo pipefail

SERVICE_NAME="photoprivacy"
UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
INSTALL_DIR="/opt/photoprivacy"
HOT_FOLDER="/var/lib/photoprivacy/hot"
AUDIT_FOLDER="/var/log/photoprivacy"
QUARANTINE_FOLDER="/var/lib/photoprivacy/quarantine"
CONFIG_PATH="${INSTALL_DIR}/config/config.json"
SERVICE_USER="photoprivacy"
SERVICE_GROUP="photoprivacy"
# socket 访问组独立于数据组：GUI 用户只进这个组，拿不到 hot/quarantine 的组写权限
# （评审 CWE-732：同组可绕过 IPC 直接删审计与隔离文件）。
SOCKET_GROUP="photoprivacy-ipc"
WORKER_BIN="${INSTALL_DIR}/PhotoPrivacyWorker"
# Worker Serilog 落点：相对 CWD 的 logs/worker-.log（Program.cs ResolveLogFilePath 非 Windows 分支），
# systemd WorkingDirectory=INSTALL_DIR（install 脚本写入的 unit）→ INSTALL_DIR/logs/worker-*.log。
WORKER_LOG_GLOB="${INSTALL_DIR}/logs/worker-*.log"
# 服务模式 UDS 端点（WorkerIpcEndpointNames.LinuxServiceSocket）：systemd RuntimeDirectory 落点，
# 与调用者身份无关。
SERVICE_SOCKET="/run/photoprivacy/worker.sock"
SERVICE_SOCKET_DIR="/run/photoprivacy"

RELEASE_DIR=""
EVIDENCE_DIR=""

usage() {
  cat <<EOF
Usage: sudo $0 --release-dir <dir> [--evidence-dir <dir>]

Options:
  --release-dir <dir>   publish.sh 产出目录（须含 PhotoPrivacyWorker 与 config/config.json）
  --evidence-dir <dir>  证据落盘目录（默认: logs/systemd-verify）
  -h, --help            Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release-dir) RELEASE_DIR="$2"; shift 2 ;;
    --evidence-dir) EVIDENCE_DIR="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
INSTALL_SCRIPT="${SCRIPT_DIR}/install-systemd-service.sh"

# ---------- 预检 ----------
if [[ "$(uname -s)" != "Linux" ]]; then
  echo "本 harness 只能在 Linux 上运行（实机或 CI Ubuntu runner），当前: $(uname -s)" >&2
  exit 1
fi
if [[ $EUID -ne 0 ]]; then
  echo "Please run as root (sudo)." >&2
  exit 1
fi
if [[ ! -d /run/systemd/system ]]; then
  echo "systemd 未运行（/run/systemd/system 缺失）——本 harness 需要 systemd 为 init 的完整 VM。" >&2
  exit 1
fi
if [[ -z "$RELEASE_DIR" ]]; then
  echo "Missing required --release-dir" >&2
  usage
  exit 1
fi
if [[ ! -f "${RELEASE_DIR}/PhotoPrivacyWorker" ]]; then
  echo "Missing executable in release dir: ${RELEASE_DIR}/PhotoPrivacyWorker" >&2
  echo "先运行 scripts/publish.sh --runtime linux-x64 产出发布目录。" >&2
  exit 1
fi
if [[ ! -f "${RELEASE_DIR}/config/config.json" ]]; then
  echo "Missing ${RELEASE_DIR}/config/config.json（publish.sh 会从 config.sample.json 复制）" >&2
  exit 1
fi
if [[ ! -f "$INSTALL_SCRIPT" ]]; then
  echo "Missing install script: ${INSTALL_SCRIPT}" >&2
  exit 1
fi

if [[ -z "$EVIDENCE_DIR" ]]; then
  EVIDENCE_DIR="logs/systemd-verify"
fi
mkdir -p "$EVIDENCE_DIR"
SUMMARY_FILE="${EVIDENCE_DIR}/summary.txt"
: > "$SUMMARY_FILE"

PASS_COUNT=0
FAIL_COUNT=0

rec() {
  # rec PASS|FAIL|INFO <描述>
  printf '%s  %s\n' "$1" "$2" | tee -a "$SUMMARY_FILE"
  case "$1" in
    PASS) PASS_COUNT=$((PASS_COUNT + 1)) ;;
    FAIL) FAIL_COUNT=$((FAIL_COUNT + 1)) ;;
  esac
}

capture() {
  # capture <证据文件> <命令...>：命令原样输出留证（失败不中断 harness，退出码记入证据）
  local out="$EVIDENCE_DIR/$1"
  shift
  local rc=0
  {
    echo "\$ $*"
    "$@" 2>&1 || rc=$?
    echo "[exit ${rc}]"
  } >> "$out"
  return 0
}

# ---------- GUI 用户（安装脚本 --gui-user 的显式语义） ----------
# harness 要求 sudo 从 GUI 用户运行，SUDO_USER 即调用者；这里显式传给安装脚本，让 --gui-user 的显式
# 分支（指定用户不存在/加不进组必须让安装失败）在 CI 里真的被走到，而不是全程只靠 SUDO_USER 推断兜底。
GUI_USER="${SUDO_USER:-}"
INSTALL_GUI_ARGS=()
if [[ -n "$GUI_USER" ]]; then
  INSTALL_GUI_ARGS=(--gui-user "$GUI_USER")
fi

# 负向自证（早于铺发布包，安装脚本在参数解析阶段拒绝，无任何账户/目录副作用）：
# 1) 显式空 --gui-user 必须硬失败（macOS #180 评审 R5 回归锚点，Linux 同款）——早前它会静默落进
#    「未解析到 GUI 用户」的告警分支并以 exit 0 收场，调用方会把「没给名字」误判成「权限已供给」。
if bash "$INSTALL_SCRIPT" --gui-user "" > "$EVIDENCE_DIR/00-negative-gui-user-empty.txt" 2>&1; then
  rec FAIL "install-systemd-service.sh 接受了空 --gui-user（期望非零退出）"
else
  rec PASS "空 --gui-user 被拒绝（显式语义不再静默降级成告警+成功）"
fi
# 2) --socket-group 与数据组同名必须拒绝——否则 GUI 用户经 socket 组拿到 hot/quarantine 的 2770 组写。
if bash "$INSTALL_SCRIPT" --socket-group "$SERVICE_GROUP" > "$EVIDENCE_DIR/00-negative-socket-group-same.txt" 2>&1; then
  rec FAIL "install-systemd-service.sh 接受了与数据组同名的 --socket-group（期望非零退出）"
else
  rec PASS "同名 --socket-group 被拒绝（组隔离不静默退化）"
fi
# 3) --socket-group 空值必须拒绝。
if bash "$INSTALL_SCRIPT" --socket-group "" > "$EVIDENCE_DIR/00-negative-socket-group-empty.txt" 2>&1; then
  rec FAIL "install-systemd-service.sh 接受了空 --socket-group（期望非零退出）"
else
  rec PASS "空 --socket-group 被拒绝"
fi

probe_raw() {
  # systemctl is-active 的退出码与 stdout（LinuxProbe::state 同款查询）
  set +e
  PROBE_OUT="$(systemctl is-active "$SERVICE_NAME" 2>/dev/null)"
  PROBE_RC=$?
  set -e
}

probe_state() {
  # 与 LinuxProbe::state + parse_systemctl_active 同判定：退出码 0=Running / 3=Stopped /
  # 4=NotInstalled，其余按 stdout 文本（active/inactive/failed/unknown）。
  probe_raw
  case "$PROBE_RC" in
    0) echo "Running" ;;
    3) echo "Stopped" ;;
    4) echo "NotInstalled" ;;
    *)
      case "$(printf '%s' "$PROBE_OUT" | tr -d '[:space:]')" in
        active) echo "Running" ;;
        inactive|failed) echo "Stopped" ;;
        unknown) echo "NotInstalled" ;;
        *) echo "Unknown" ;;
      esac
      ;;
  esac
}

state_is() {
  [[ "$(probe_state)" == "$1" ]]
}

wait_until() {
  # wait_until <秒> <命令...>：命令成功即返回 0；超时返回 1
  local timeout="$1"
  shift
  local deadline=$(( $(date +%s) + timeout ))
  while true; do
    if "$@" >/dev/null 2>&1; then
      return 0
    fi
    if [[ "$(date +%s)" -ge "$deadline" ]]; then
      return 1
    fi
    sleep 1
  done
}

expect_state() {
  # expect_state <期望状态> <描述>
  local expected="$1" desc="$2" actual
  actual="$(probe_state)"
  if [[ "$actual" == "$expected" ]]; then
    rec PASS "${desc}（状态=${actual}）"
  else
    rec FAIL "${desc}（期望 ${expected}，实测 ${actual}）"
  fi
}

get_pid() {
  # 服务主进程 PID（systemd MainPID；未运行则 0/空）
  local pid
  pid="$(systemctl show "$SERVICE_NAME" -p MainPID --value 2>/dev/null)" || true
  printf '%s\n' "$pid"
}

worker_logs_present() {
  # shellcheck disable=SC2086  # 有意走 glob
  local f
  for f in $WORKER_LOG_GLOB; do
    if [[ -f "$f" ]] && [[ -s "$f" ]]; then
      return 0
    fi
  done
  return 1
}

audit_files_present() {
  local f
  for f in "$AUDIT_FOLDER"/audit-*; do
    if [[ -f "$f" ]]; then
      return 0
    fi
  done
  return 1
}

worker_absent() {
  ! pgrep -f "${WORKER_BIN}" >/dev/null 2>&1
}

ui_probe_report() {
  # UI 层 Service 页同源探测（--service-probe-report --service-probe-ipc）与 CLI probe_state 对拍：
  # 一致 = 「新 UI Service 页探测到服务状态」的机器可读证据（建窗前退出，无头 runner 可跑）。
  # 探测以 GUI 用户语境运行（sudo -u，新进程取新组身份）——对拍结果才代表真实 Service 页行为；
  # root 语境探测会掩盖组边界缺陷。第二参数 expect_reachable=1 时要求 IPC 真的可达（仅服务应运行阶段）。
  local tag="$1"
  local expect_reachable="${2:-0}"
  if [[ -z "${SUDO_USER:-}" ]] || [[ "$SUDO_USER" == "root" ]]; then
    rec FAIL "UI 探测 ${tag}：需经 sudo 从 GUI 用户运行（SUDO_USER 缺失）——root 语境对拍不代表真实 Service 页行为"
    return 0
  fi
  if [[ ! -x "${INSTALL_DIR}/PhotoPrivacy" ]]; then
    rec FAIL "UI 探测 ${tag}：UI 可执行文件缺失（${INSTALL_DIR}/PhotoPrivacy）"
    return 0
  fi
  local user_dir report out rc
  # GUI 用户只拿专用子目录可写，root 侧证据文件不交出去（CWE-59，与 macOS harness 同口径）；
  # 报告由 root 收回证据根（rename 不跟随符号链接，无截断面）。
  user_dir="$EVIDENCE_DIR/ui-user"
  mkdir -p "$user_dir"
  chown "$SUDO_USER" "$user_dir" 2>/dev/null || true
  report="$user_dir/ui-probe-$tag.json"
  out="$EVIDENCE_DIR/ui-probe-$tag.txt"
  rm -f "$report"
  rc=0
  timeout -k 5 30 sudo -u "$SUDO_USER" \
    "${INSTALL_DIR}/PhotoPrivacy" \
    --service-probe-report "$report" \
    --service-probe-ipc \
    --worker "$user_dir/no-worker-for-probe" > "$out" 2>&1 || rc=$?
  if [[ -f "$report" ]]; then
    mv -f "$report" "$EVIDENCE_DIR/ui-probe-$tag.json"
    report="$EVIDENCE_DIR/ui-probe-$tag.json"
  fi
  # 命令失败/超时（rc!=0）时报告不作数：异常退出/超时杀后的残留报告即使文件在、状态对拍一致
  # 也不得洗成 PASS（macOS #175 审查意见同款，fail-closed）。
  if [[ $rc -ne 0 ]]; then
    rec FAIL "UI Service 页探测命令失败/超时（${tag}: rc=${rc}）——报告不采信，见 ui-probe-$tag.txt"
    return 0
  fi
  if [[ ! -f "$report" ]]; then
    rec FAIL "UI Service 页探测报告未产出（${tag}，rc=${rc}，见 ui-probe-$tag.txt）"
    return 0
  fi
  local ui_state cli_state ui_ipc ui_endpoint
  ui_state="$(sed -n 's/.*"state":"\([A-Za-z]*\)".*/\1/p' "$report")"
  cli_state="$(probe_state)"
  if [[ -n "$ui_state" ]] && [[ "$ui_state" == "$cli_state" ]]; then
    rec PASS "UI Service 页探测与 systemctl 判定一致（${tag}: ${ui_state}，GUI 用户语境）"
  else
    rec FAIL "UI/CLI 探测分歧（${tag}: ui=${ui_state:-<空>} cli=${cli_state}）"
  fi
  # Linux 服务端点与调用者身份无关（RuntimeDirectory 系统级落点）：GUI 用户语境的客户端必须
  # 解析到安装链建的那个 socket，而不是任何 per-user 路径。
  ui_ipc="$(sed -n 's/.*"ipc":"\([A-Za-z]*\)".*/\1/p' "$report")"
  ui_endpoint="$(sed -n 's/.*"endpoint":"\([^"]*\)".*/\1/p' "$report")"
  if [[ "$ui_endpoint" == "$SERVICE_SOCKET" ]]; then
    rec PASS "UI 服务模式端点解析一致（${tag}: ${ui_endpoint}，GUI 用户语境）"
  else
    rec FAIL "UI 服务模式端点解析分歧（${tag}: endpoint=${ui_endpoint:-<空>}，期望 ${SERVICE_SOCKET}）"
  fi
  if [[ "$expect_reachable" == 1 ]]; then
    # 运行态必须真的连上：目录 0750 / socket 0660 / 组边界 / 服务未就绪，任一项不成立都会在这里露头。
    if [[ "$ui_ipc" == "Reachable" ]]; then
      rec PASS "UI 服务模式 IPC 可达（${tag}: ${ui_endpoint}）"
    else
      rec FAIL "UI 服务模式 IPC 不可达（${tag}: ipc=${ui_ipc:-<空>}，见 ui-probe-$tag.json）"
    fi
  elif [[ "$ui_ipc" == "Reachable" ]]; then
    # 未装/已停/已卸载阶段仍有人应答，说明进程或 socket 残留，服务页会给出假的「运行中」。
    rec FAIL "UI 服务模式 IPC 在服务不应运行阶段仍可达（${tag}）——疑似进程或 socket 残留"
  else
    rec PASS "UI 服务模式 IPC 在非运行阶段不可达（${tag}: ${ui_ipc:-<空>}）"
  fi
}

new_pid_alive() {
  # 旧 PID 已死、systemd 已拉起新 PID
  local p
  p="$(get_pid)"
  if [[ -z "$p" ]] || [[ "$p" == "0" ]] || [[ "$p" == "$OLD_PID" ]]; then
    return 1
  fi
  ps -p "$p" >/dev/null 2>&1
}

cleanup() {
  # 兜底：任一环节失败也不留运行中的服务（与产品卸载语义一致：stop + disable + 删 unit +
  # daemon-reload；对已卸载态幂等——失败一律吞掉）。
  systemctl stop "$SERVICE_NAME" >/dev/null 2>&1 || true
  systemctl disable "$SERVICE_NAME" >/dev/null 2>&1 || true
  rm -f "$UNIT_PATH" >/dev/null 2>&1 || true
  systemctl daemon-reload >/dev/null 2>&1 || true
}
trap cleanup EXIT

# ---------- 0. 环境留证（虚拟环境/实机识别） ----------
capture 00-env.txt uname -a
capture 00-env.txt cat /etc/os-release
capture 00-env.txt systemctl --version

# ---------- 1. staging：按用户口径把发布包解压进安装目录 ----------
capture 01-staging.txt echo "release dir: ${RELEASE_DIR}"
mkdir -p "$INSTALL_DIR"
set +e
cp -R "${RELEASE_DIR}/." "${INSTALL_DIR}/" >> "${EVIDENCE_DIR}/01-staging.txt" 2>&1
STAGE_RC=$?
set -e
echo "[exit ${STAGE_RC}]" >> "${EVIDENCE_DIR}/01-staging.txt"
if [[ $STAGE_RC -eq 0 ]]; then
  chmod +x "$WORKER_BIN"
  capture 01-staging.txt ls -la "$INSTALL_DIR"
  rec PASS "发布包已解压到 ${INSTALL_DIR}"
else
  rec FAIL "发布包解压失败（exit ${STAGE_RC}）——中止"
  echo "---- summary ----" | tee -a "$SUMMARY_FILE"
  echo "PASS=${PASS_COUNT} FAIL=${FAIL_COUNT}" | tee -a "$SUMMARY_FILE"
  exit 1
fi

# UI 层取证基线：安装前 = 未安装态（Service 页同源探测）
ui_probe_report pre-install

# ---------- 2. 安装（install-systemd-service.sh，LinuxOps::install 同款通道） ----------
{
  echo "---- install 前账户现状（诊断） ----"
  echo '$ getent group '"${SERVICE_GROUP}"
  getent group "$SERVICE_GROUP" 2>&1 || true
  echo '$ getent group '"${SOCKET_GROUP}"
  getent group "$SOCKET_GROUP" 2>&1 || true
  echo '$ id '"${SERVICE_USER}"
  id "$SERVICE_USER" 2>&1 || true
  echo "---- install -x trace ----"
} > "${EVIDENCE_DIR}/02-install.txt"
set +e
bash -x "$INSTALL_SCRIPT" \
  --install-dir "$INSTALL_DIR" \
  --config "$CONFIG_PATH" \
  --hot-folder "$HOT_FOLDER" \
  --audit-folder "$AUDIT_FOLDER" \
  --quarantine "$QUARANTINE_FOLDER" \
  --user "$SERVICE_USER" \
  --group "$SERVICE_GROUP" \
  --socket-group "$SOCKET_GROUP" \
  ${INSTALL_GUI_ARGS[@]+"${INSTALL_GUI_ARGS[@]}"} >> "${EVIDENCE_DIR}/02-install.txt" 2>&1
INSTALL_RC=$?
set -e
echo "[exit ${INSTALL_RC}]" >> "${EVIDENCE_DIR}/02-install.txt"
if [[ $INSTALL_RC -eq 0 ]]; then
  rec PASS "install-systemd-service.sh 执行成功"
else
  rec FAIL "install-systemd-service.sh 执行失败（exit ${INSTALL_RC}）"
fi

if [[ ! -f "$UNIT_PATH" ]]; then
  rec FAIL "unit 未写入 ${UNIT_PATH}——后续生命周期断言无意义，提前收口"
  echo "---- summary ----" | tee -a "$SUMMARY_FILE"
  echo "PASS=${PASS_COUNT} FAIL=${FAIL_COUNT}" | tee -a "$SUMMARY_FILE"
  exit 1
fi
rec PASS "unit 已写入 ${UNIT_PATH}"

if [[ "$(systemctl is-enabled "$SERVICE_NAME" 2>/dev/null)" == "enabled" ]]; then
  rec PASS "服务已 enable（开机自启）"
else
  rec FAIL "服务未 enable（is-enabled=$(systemctl is-enabled "$SERVICE_NAME" 2>/dev/null)）"
fi

# ---------- 3. 注册 + 常驻 ----------
if wait_until 30 state_is Running; then
  rec PASS "systemd 注册且运行中（systemctl is-active ${SERVICE_NAME} = active）"
else
  rec FAIL "安装后 30s 内未进入 Running（probe=$(probe_state)）"
fi
capture 03-systemctl-after-install.txt systemctl status "$SERVICE_NAME" --no-pager
capture 03-systemctl-after-install.txt systemctl cat "$SERVICE_NAME" --no-pager

OLD_PID="$(get_pid)"
if [[ -n "$OLD_PID" ]] && [[ "$OLD_PID" != "0" ]] && ps -p "$OLD_PID" >/dev/null 2>&1; then
  rec PASS "Worker 进程存活（pid=${OLD_PID}）"
else
  rec FAIL "取不到存活的 Worker 进程（pid='${OLD_PID}'）"
fi

# 稳定性：5s 后 PID 不变才不是崩溃重启循环（Restart=always 会把崩溃循环伪装成 Running）
sleep 5
STABLE_PID="$(get_pid)"
if [[ -n "$OLD_PID" ]] && [[ "$STABLE_PID" == "$OLD_PID" ]] && ps -p "$STABLE_PID" >/dev/null 2>&1; then
  rec PASS "Worker 进程稳定（5s 内 PID 不变=${STABLE_PID}）"
else
  rec FAIL "Worker 进程不稳：5s 内 PID ${OLD_PID}→${STABLE_PID}（疑似启动后崩溃循环）"
fi
OLD_PID="$STABLE_PID"

# UI 层取证：运行中态（Service 页同源探测，IPC 必须可达），在 Restart 试验前取稳定态
ui_probe_report running 1

# Restart=always 常驻：SIGKILL 后 systemd 应拉起新进程（RestartSec=3）
if [[ -n "$OLD_PID" ]] && [[ "$OLD_PID" != "0" ]]; then
  kill -9 "$OLD_PID" >> "${EVIDENCE_DIR}/04-restart.txt" 2>&1 || true
  echo "killed pid=${OLD_PID} at $(date '+%H:%M:%S')" >> "${EVIDENCE_DIR}/04-restart.txt"
  if wait_until 45 new_pid_alive; then
    NEW_PID="$(get_pid)"
    rec PASS "Restart=always 常驻：SIGKILL 后 systemd 重启（${OLD_PID} → ${NEW_PID}）"
  else
    rec FAIL "SIGKILL 后 45s 内 systemd 未重启（probe=$(probe_state)）"
  fi
else
  rec FAIL "Restart 重启未测：无可杀的 Worker 进程"
fi
capture 04-restart.txt systemctl status "$SERVICE_NAME" --no-pager

# ---------- 4. 日志 ----------
if wait_until 30 worker_logs_present; then
  rec PASS "Worker Serilog 日志已落盘（${WORKER_LOG_GLOB}）"
else
  rec FAIL "30s 内未见 Worker Serilog 日志（${WORKER_LOG_GLOB}）"
fi
capture 05-logs.txt journalctl -u "$SERVICE_NAME" --no-pager -n 50
capture 05-logs.txt ls -la "$AUDIT_FOLDER"
# shellcheck disable=SC2086
capture 05-logs.txt ls -la ${INSTALL_DIR}/logs
{
  echo "---- worker serilog ----"
  # shellcheck disable=SC2086
  tail -n 20 $WORKER_LOG_GLOB 2>/dev/null || true
} >> "${EVIDENCE_DIR}/05-logs.txt"
if grep -q "no watch folder is configured" ${WORKER_LOG_GLOB} 2>/dev/null; then
  rec INFO "Worker 启动待机行在列（no watch folder is configured）"
fi

# ---------- 5. socket + 权限组边界（#188 核心：逐项对拍 mode/group） ----------
capture 06-socket.txt ls -la "$SERVICE_SOCKET_DIR"
if wait_until 30 test -S "$SERVICE_SOCKET"; then
  rec PASS "服务模式 UDS 端点已绑定（${SERVICE_SOCKET}）"
  # 组边界必须真的落在盘上——socket 0660 + 服务用户/socket 访问组，目录 0750。
  # 权限偏了（umask 收成 0600、目录被改成 0755、属组退回数据组）意味着 GUI 用户要么连不上、
  # 要么谁都能连；这里按安装脚本与 UnixDomainSocketIpcTransport 的承诺逐项对拍。
  SOCKET_MODE="$(stat -c '%a' "$SERVICE_SOCKET" 2>/dev/null)"
  SOCKET_OWNER="$(stat -c '%U:%G' "$SERVICE_SOCKET" 2>/dev/null)"
  SOCKET_DIR_MODE="$(stat -c '%a' "$SERVICE_SOCKET_DIR" 2>/dev/null)"
  SOCKET_DIR_OWNER="$(stat -c '%U:%G' "$SERVICE_SOCKET_DIR" 2>/dev/null)"
  if [[ "$SOCKET_MODE" == "660" ]] \
    && [[ "$SOCKET_OWNER" == "${SERVICE_USER}:${SOCKET_GROUP}" ]] \
    && [[ "$SOCKET_DIR_MODE" == "750" ]] \
    && [[ "$SOCKET_DIR_OWNER" == "${SERVICE_USER}:${SOCKET_GROUP}" ]]; then
    rec PASS "服务模式 UDS 权限符合组边界（socket ${SOCKET_MODE} ${SOCKET_OWNER}，目录 ${SOCKET_DIR_MODE} ${SOCKET_DIR_OWNER}）"
  else
    rec FAIL "服务模式 UDS 权限不符（socket=${SOCKET_MODE:-<空>} ${SOCKET_OWNER:-<空>}，目录=${SOCKET_DIR_MODE:-<空>} ${SOCKET_DIR_OWNER:-<空>}；期望 660 ${SERVICE_USER}:${SOCKET_GROUP} / 750 ${SERVICE_USER}:${SOCKET_GROUP}）"
  fi

  # 审计目录：组换 socket 访问组、2750（组只读）、文件 0640——能看日志页，不能写、不能删。
  AUDIT_MODE="$(stat -c '%a' "$AUDIT_FOLDER" 2>/dev/null)"
  AUDIT_OWNER="$(stat -c '%U:%G' "$AUDIT_FOLDER" 2>/dev/null)"
  if [[ "$AUDIT_MODE" == "2750" ]] && [[ "$AUDIT_OWNER" == "${SERVICE_USER}:${SOCKET_GROUP}" ]]; then
    rec PASS "审计目录组边界正确（${AUDIT_FOLDER} ${AUDIT_MODE} ${AUDIT_OWNER}：组只读，无组写）"
  else
    rec FAIL "审计目录组边界不符（${AUDIT_FOLDER} mode=${AUDIT_MODE:-<空>} owner=${AUDIT_OWNER:-<空>}；期望 2750 ${SERVICE_USER}:${SOCKET_GROUP}）"
  fi
  # 运行期审计文件（running 探针的 GetStatus 触发 exiftool_started 审计后应已出现）：
  # 有文件则硬断言 0640 + socket 组；无文件记 INFO——文件 0640 的运行期口径（UMask=0027）
  # 已由 Worker Serilog 日志文件硬断言覆盖（同一进程 umask），安装期既有文件口径由安装脚本 chmod 覆盖。
  if audit_files_present; then
    AUDIT_FILE_MODE="$(find "$AUDIT_FOLDER" -maxdepth 1 -type f -name 'audit-*' -exec stat -c '%a' {} + 2>/dev/null | sort -u | tr '\n' ' ')"
    AUDIT_FILE_GROUP="$(find "$AUDIT_FOLDER" -maxdepth 1 -type f -name 'audit-*' -exec stat -c '%G' {} + 2>/dev/null | sort -u | tr '\n' ' ')"
    if [[ "$AUDIT_FILE_MODE" == "640 " ]] && [[ "$AUDIT_FILE_GROUP" == "${SOCKET_GROUP} " ]]; then
      rec PASS "审计文件组只读（mode=${AUDIT_FILE_MODE% } group=${AUDIT_FILE_GROUP% }）"
    else
      rec FAIL "审计文件权限不符（mode=${AUDIT_FILE_MODE:-<空>} group=${AUDIT_FILE_GROUP:-<空>}；期望 640 ${SOCKET_GROUP}）"
    fi
  else
    rec INFO "暂无审计文件（无处理事件）；文件 0640 运行期口径由 Worker 日志断言覆盖（同进程 UMask=0027）"
  fi

  # 安装目录与 Worker 日志（macOS R1 同款）：安装目录 0755（GUI 用户要执行其中的 UI 二进制，
  # 0750 会把 UI 一起锁死）+ 敏感面 config/ 750/640、logs/ 2750/640，逐项对拍。
  INSTALL_MODE="$(stat -c '%a' "$INSTALL_DIR" 2>/dev/null)"
  WLOG_DIR_MODE="$(stat -c '%a' "${INSTALL_DIR}/logs" 2>/dev/null)"
  # shellcheck disable=SC2086
  WLOG_MODE="$(stat -c '%a' ${WORKER_LOG_GLOB} 2>/dev/null | sort -u | tr '\n' ' ')"
  CFG_DIR_MODE="$(stat -c '%a' "${INSTALL_DIR}/config" 2>/dev/null)"
  CFG_FILE_MODE="$(find "${INSTALL_DIR}/config" -maxdepth 1 -type f -exec stat -c '%a' {} + 2>/dev/null | sort -u | tr '\n' ' ')"
  if [[ "$INSTALL_MODE" == "755" ]] \
    && [[ "$WLOG_DIR_MODE" == "2750" ]] \
    && [[ "$WLOG_MODE" == "640 " ]] \
    && [[ "$CFG_DIR_MODE" == "750" ]] \
    && [[ "$CFG_FILE_MODE" == "640 " ]]; then
    rec PASS "安装目录权限收敛（install ${INSTALL_MODE}，config ${CFG_DIR_MODE}/${CFG_FILE_MODE% }，logs ${WLOG_DIR_MODE}，日志文件 ${WLOG_MODE% }）"
  else
    rec FAIL "安装目录/日志权限未收敛（install=${INSTALL_MODE:-<空>} config=${CFG_DIR_MODE:-<空>}/${CFG_FILE_MODE:-<空>} logs=${WLOG_DIR_MODE:-<空>} file=${WLOG_MODE:-<空>}；期望 755 / 750+640 / 2750 / 640）"
  fi

  # hot/quarantine 保持数据组 2770：GUI 用户不进数据组就完全不可见。
  HOT_MODE="$(stat -c '%a' "$HOT_FOLDER" 2>/dev/null)"
  HOT_OWNER="$(stat -c '%U:%G' "$HOT_FOLDER" 2>/dev/null)"
  QUAR_MODE="$(stat -c '%a' "$QUARANTINE_FOLDER" 2>/dev/null)"
  QUAR_OWNER="$(stat -c '%U:%G' "$QUARANTINE_FOLDER" 2>/dev/null)"
  if [[ "$HOT_MODE" == "2770" ]] && [[ "$HOT_OWNER" == "${SERVICE_USER}:${SERVICE_GROUP}" ]] \
    && [[ "$QUAR_MODE" == "2770" ]] && [[ "$QUAR_OWNER" == "${SERVICE_USER}:${SERVICE_GROUP}" ]]; then
    rec PASS "hot/quarantine 保持数据组 2770（${HOT_MODE} ${HOT_OWNER} / ${QUAR_MODE} ${QUAR_OWNER}）"
  else
    rec FAIL "hot/quarantine 权限不符（hot=${HOT_MODE:-<空>} ${HOT_OWNER:-<空>} quarantine=${QUAR_MODE:-<空>} ${QUAR_OWNER:-<空>}；期望 2770 ${SERVICE_USER}:${SERVICE_GROUP}）"
  fi

  # unit 注入必须真的落盘（票面缺陷：注入本身不产生组隔离效果——这里同时验注入与盘上效果）：
  # Group=socket 组决定 socket/RuntimeDirectory 属组；Environment 白名单跟组走；UMask=0027 收运行期新文件。
  UNIT_OK=1
  for needle in \
    "User=${SERVICE_USER}" \
    "Group=${SOCKET_GROUP}" \
    "Environment=PHOTOPRIVACY_SERVICE_GROUP=${SOCKET_GROUP}" \
    "UMask=0027" \
    "RuntimeDirectoryMode=0750"; do
    if ! grep -qF "$needle" "$UNIT_PATH"; then
      UNIT_OK=0
      rec FAIL "unit 缺少注入：${needle}"
    fi
  done
  if [[ "$UNIT_OK" == 1 ]]; then
    rec PASS "unit 注入齐全（User/Group=socket 组/PHOTOPRIVACY_SERVICE_GROUP/UMask=0027/RuntimeDirectoryMode=0750）"
  fi

  # 功能面 + GUI 组边界：GUI_USER 是 SUDO_USER 的受控投影。root 直登或 SUDO_USER=root 无法代表
  # 真实 GUI 用户，与 ui_probe_report 同口径记 FAIL，而不是静默跳过（fail-closed）。
  if [[ -z "$GUI_USER" || "$GUI_USER" == "root" ]]; then
    rec FAIL "GUI 组边界未验证：需经 sudo 从 GUI 用户运行（GUI_USER 缺失或为 root）"
  else
    if sudo -u "$GUI_USER" test -x "${INSTALL_DIR}/PhotoPrivacy"; then
      rec PASS "GUI 用户 ${GUI_USER} 可执行安装目录内的 UI 二进制（${INSTALL_DIR}/PhotoPrivacy）"
    else
      rec FAIL "GUI 用户 ${GUI_USER} 无法执行 ${INSTALL_DIR}/PhotoPrivacy——安装目录收紧过头，UI 无法启动"
    fi
    # GUI 用户只应进 socket 访问组：进了数据组就等于拿到 2770 的组写权限，可绕过 IPC 删审计/隔离文件。
    if id -nG "$GUI_USER" | tr ' ' '\n' | grep -qx "$SOCKET_GROUP"; then
      rec PASS "GUI 用户 ${GUI_USER} 已在 socket 访问组 ${SOCKET_GROUP}"
    else
      rec FAIL "GUI 用户 ${GUI_USER} 不在 socket 访问组 ${SOCKET_GROUP}——服务模式 UI 连不上端点"
    fi
    if id -nG "$GUI_USER" | tr ' ' '\n' | grep -qx "$SERVICE_GROUP"; then
      rec FAIL "GUI 用户 ${GUI_USER} 仍在数据组 ${SERVICE_GROUP}——可绕过 IPC 直接删审计/隔离文件"
    else
      rec PASS "GUI 用户 ${GUI_USER} 未获授数据组 ${SERVICE_GROUP}（无组写）"
    fi
    # 审计目录：可读（日志页本地 tail 通道）、不可写不可删（目录组 r-x）。
    if sudo -u "$GUI_USER" test -r "$AUDIT_FOLDER"; then
      rec PASS "GUI 用户可读审计目录（${AUDIT_FOLDER}，日志页本地 tail 通道）"
    else
      rec FAIL "GUI 用户不可读审计目录 ${AUDIT_FOLDER}——日志页本地 tail 通道断了"
    fi
    if sudo -u "$GUI_USER" test -w "$AUDIT_FOLDER"; then
      rec FAIL "GUI 用户可写审计目录 ${AUDIT_FOLDER}——可绕过 IPC 篡改/删除审计"
    else
      rec PASS "GUI 用户不可写审计目录（删不掉、改不了审计文件）"
    fi
    if audit_files_present; then
      AUDIT_FIRST="$(find "$AUDIT_FOLDER" -maxdepth 1 -type f -name 'audit-*' | head -n 1)"
      if sudo -u "$GUI_USER" test -r "$AUDIT_FIRST"; then
        rec PASS "GUI 用户可读审计文件（${AUDIT_FIRST}）"
      else
        rec FAIL "GUI 用户不可读审计文件 ${AUDIT_FIRST}——日志页本地 tail 通道断了"
      fi
    fi
    # 敏感面：config 与 Worker 日志对 GUI 用户不可读（服务配置写入边界，#167 决策）；
    # hot/quarantine 对 GUI 用户不可遍历（数据组 2770，GUI 不在数据组）。
    CFG_FIRST="$(find "${INSTALL_DIR}/config" -maxdepth 1 -type f -name 'config.json' | head -n 1)"
    if [[ -n "$CFG_FIRST" ]] && sudo -u "$GUI_USER" test -r "$CFG_FIRST"; then
      rec FAIL "GUI 用户可读 ${CFG_FIRST}——服务配置边界被放宽（config 含 exiftool.path，可读即可探测服务行为）"
    else
      rec PASS "GUI 用户不可读服务配置（config/ 0750+0640 数据组）"
    fi
    # shellcheck disable=SC2086
    WLOG_FIRST="$(ls ${WORKER_LOG_GLOB} 2>/dev/null | head -n 1)"
    if [[ -n "$WLOG_FIRST" ]] && sudo -u "$GUI_USER" test -r "$WLOG_FIRST"; then
      rec FAIL "GUI 用户可读 Worker 日志 ${WLOG_FIRST}——请求 URL/文件名/错误栈外泄"
    else
      rec PASS "GUI 用户不可读 Worker 日志（logs/ 2750+0640 数据组）"
    fi
    if sudo -u "$GUI_USER" test -x "$HOT_FOLDER"; then
      rec FAIL "GUI 用户可遍历 hot 目录 ${HOT_FOLDER}——数据组边界被放宽"
    else
      rec PASS "GUI 用户不可遍历 hot 目录（数据组 2770）"
    fi
    if sudo -u "$GUI_USER" test -x "$QUARANTINE_FOLDER"; then
      rec FAIL "GUI 用户可遍历 quarantine 目录 ${QUARANTINE_FOLDER}——数据组边界被放宽"
    else
      rec PASS "GUI 用户不可遍历 quarantine 目录（数据组 2770）"
    fi
  fi

  # 收敛不能打断数据路径：服务用户（属主）对 hot/quarantine/audit/logs 仍必须可写。
  SVC_WRITE_OK=1
  for d in "$HOT_FOLDER" "$QUARANTINE_FOLDER" "$AUDIT_FOLDER" "${INSTALL_DIR}/logs"; do
    if ! sudo -u "$SERVICE_USER" test -w "$d"; then
      SVC_WRITE_OK=0
      rec FAIL "服务用户不可写 ${d}——权限收敛打断了 Worker 数据路径"
    fi
  done
  if [[ "$SVC_WRITE_OK" == 1 ]]; then
    rec PASS "服务用户对 hot/quarantine/audit/logs 仍可写（属主身份，收敛不打断数据路径）"
  fi

  # 组隔离的第二道事实：socket 访问组与数据组必须是两条不同 GID 的组记录。
  # 同名/同 GID 的崩坏形态会让 GUI 用户虽然只进了 socket 组，却继承数据组对 hot/quarantine 的 2770
  # 组写（macOS #180 评审 Major 同款）。这里按 getent 记录对拍，不依赖安装器自述。
  SOCKET_GID="$(getent group "$SOCKET_GROUP" | cut -d: -f3)"
  SERVICE_GID="$(getent group "$SERVICE_GROUP" | cut -d: -f3)"
  if [[ -n "$SOCKET_GID" && -n "$SERVICE_GID" && "$SOCKET_GID" != "$SERVICE_GID" ]]; then
    rec PASS "socket 访问组 GID 独立于数据组（${SOCKET_GROUP}=${SOCKET_GID} ≠ ${SERVICE_GROUP}=${SERVICE_GID}）"
  else
    rec FAIL "socket 访问组 GID 未与数据组分离（${SOCKET_GROUP}=${SOCKET_GID:-<空>}，${SERVICE_GROUP}=${SERVICE_GID:-<空>}）——同 GID 等于同组权限"
  fi
else
  capture 06-socket.txt find "$SERVICE_SOCKET_DIR" "${INSTALL_DIR}" \( -name "*.sock" -o -name "*.log" \)
  CUR_PID="$(get_pid)"
  if [[ -n "$CUR_PID" ]] && [[ "$CUR_PID" != "0" ]]; then
    capture 06-socket.txt ps -p "$CUR_PID" -o user,group,pid,cmd
  fi
  rec FAIL "30s 内未见服务模式 UDS 端点（${SERVICE_SOCKET}）"
fi

# ---------- 6. 停止（LinuxOps::stop 同款命令串） ----------
set +e
systemctl stop "$SERVICE_NAME" > "${EVIDENCE_DIR}/07-stop.txt" 2>&1
STOP_RC=$?
set -e
echo "[exit ${STOP_RC}]" >> "${EVIDENCE_DIR}/07-stop.txt"
if [[ $STOP_RC -eq 0 ]]; then
  rec PASS "systemctl stop 停止成功"
else
  rec FAIL "systemctl stop 停止失败（exit ${STOP_RC}）"
fi
expect_state Stopped "停止后状态=已安装未运行"

# UI 层取证：已停止态
ui_probe_report stopped

# ---------- 7. 启动（LinuxOps::start 同款命令串） ----------
set +e
systemctl start "$SERVICE_NAME" > "${EVIDENCE_DIR}/08-start.txt" 2>&1
START_RC=$?
set -e
echo "[exit ${START_RC}]" >> "${EVIDENCE_DIR}/08-start.txt"
if [[ $START_RC -eq 0 ]]; then
  rec PASS "systemctl start 启动成功"
else
  rec FAIL "systemctl start 启动失败（exit ${START_RC}）"
fi
if wait_until 30 state_is Running; then
  rec PASS "再次启动恢复运行中"
else
  rec FAIL "启动后 30s 内未进入 Running（probe=$(probe_state)）"
fi

# ---------- 8. 卸载（LinuxOps::uninstall 同款命令串，stop/disable 失败不挡删 unit） ----------
set +e
sh -c "systemctl stop ${SERVICE_NAME} 2>/dev/null && \
       systemctl disable ${SERVICE_NAME} 2>/dev/null && \
       rm -f ${UNIT_PATH} && \
       systemctl daemon-reload" > "${EVIDENCE_DIR}/09-uninstall.txt" 2>&1
UNINSTALL_RC=$?
set -e
echo "[exit ${UNINSTALL_RC}]" >> "${EVIDENCE_DIR}/09-uninstall.txt"
if [[ $UNINSTALL_RC -eq 0 ]]; then
  rec PASS "卸载命令成功"
else
  rec FAIL "卸载命令失败（exit ${UNINSTALL_RC}）"
fi
expect_state NotInstalled "卸载后状态=未安装"
if [[ ! -f "$UNIT_PATH" ]]; then
  rec PASS "unit 已删除"
else
  rec FAIL "unit 仍存在：${UNIT_PATH}"
fi
if wait_until 15 worker_absent; then
  rec PASS "卸载后无 Worker 进程残留"
else
  capture 10-final.txt pgrep -fl PhotoPrivacyWorker
  rec FAIL "卸载 15s 后仍有 Worker 进程残留"
fi

# UI 层取证：卸载后 = 未安装态
ui_probe_report uninstalled

# ---------- 9. 收口 ----------
capture 10-final.txt systemctl is-active "$SERVICE_NAME"
{
  echo "---- 结论 ----"
  echo "PASS=${PASS_COUNT} FAIL=${FAIL_COUNT}"
} >> "$SUMMARY_FILE"
echo "---- summary ----"
cat "$SUMMARY_FILE"

if [[ "$FAIL_COUNT" -gt 0 ]]; then
  exit 1
fi
exit 0
