#!/usr/bin/env bash
# verify-launchd-service.sh —— #32 macOS launchd 安装/卸载/日志生命周期验证 harness。
#
# 用途：在 macOS（实机或 CI macOS runner 虚拟环境）对 launchd 服务安装链做端到端断言：
#   安装 → 注册 → 常驻（KeepAlive 重启）→ 日志 → 停止 → 启动 → 卸载 → 清理。
# 操作与判定口径对齐 ui/crates/photoprivacy-service/src/unix.rs：
#   - 安装走 scripts/install-launchd-service.sh（MacOps::install 同款，仅跳过 osascript 提权包装——
#     本 harness 本身要求 root 运行）；
#   - 停止/启动/卸载用 MacOps 同款 launchctl 命令串；
#   - 状态判定同 MacProbe::state + parse_launchctl_list：launchctl list <label> 退出码 + PID 行
#     + plist 存在性（plist 在而未加载 = 已安装未运行，不是未安装）。
# 因此脚本结论可直接代表新 UI Service 页探测/操作语义；UI 层证据两路产出：
#   - 机器可读：PhotoPrivacy --service-probe-report 与 CLI probe_state 对拍（硬断言，UI/CLI 一致）；
#   - 截图：--ui-shots 时经 capture-ui-service-page.sh 以 GUI 用户截 Service 页（内容供目检）。
# 用法：sudo ./scripts/verify-launchd-service.sh --release-dir <dir> [--evidence-dir <dir>] [--ui-shots]
#   --release-dir   publish.sh 产出目录（须含 PhotoPrivacyWorker 与 config/config.json）
#   --evidence-dir  证据落盘目录（默认 logs/launchd-verify；CI 传 $RUNNER_TEMP 下路径）
#   --ui-shots      各生命周期状态截 Service 页整屏（需经 sudo 从 GUI 用户运行；CI 开启）
#
# 注意：harness 会把发布包重新铺进 INSTALL_DIR 并整链安装/卸载，只应在实机验证或一次性
# 虚拟环境（CI macOS runner）里跑；卸载语义与产品一致（unload + 删 plist），不删服务用户与目录。
#
# 兼容 macOS 自带 bash 3.2：不使用关联数组 / mapfile / ${var,,} 等 bash>=4 语法。
set -euo pipefail

SERVICE_LABEL="com.photoprivacy.cleaner"
PLIST_PATH="/Library/LaunchDaemons/${SERVICE_LABEL}.plist"
INSTALL_DIR="/opt/photoprivacy"
HOT_FOLDER="/var/lib/photoprivacy/hot"
AUDIT_FOLDER="/var/log/photoprivacy"
QUARANTINE_FOLDER="/var/lib/photoprivacy/quarantine"
CONFIG_PATH="${INSTALL_DIR}/config/config.json"
SERVICE_USER="photoprivacy"
SERVICE_GROUP="photoprivacy"
WORKER_BIN="${INSTALL_DIR}/PhotoPrivacyWorker"
# Worker Serilog 落点：相对 CWD 的 logs/worker-.log（Program.cs ResolveLogFilePath 非 Windows 分支），
# launchd WorkingDirectory=INSTALL_DIR（install 脚本写入的 plist）→ INSTALL_DIR/logs/worker-*.log。
WORKER_LOG_GLOB="${INSTALL_DIR}/logs/worker-*.log"
# 服务模式 UDS 端点（WorkerIpcEndpointNames.MacServiceSocket，服务用户 home=NFSHomeDirectory=INSTALL_DIR）。
SERVICE_SOCKET="${INSTALL_DIR}/Library/Application Support/photoprivacy/worker.sock"

RELEASE_DIR=""
EVIDENCE_DIR=""
UI_SHOTS=0

usage() {
  cat <<EOF
Usage: sudo $0 --release-dir <dir> [--evidence-dir <dir>] [--ui-shots]

Options:
  --release-dir <dir>   publish.sh 产出目录（须含 PhotoPrivacyWorker 与 config/config.json）
  --evidence-dir <dir>  证据落盘目录（默认: logs/launchd-verify）
  --ui-shots            各生命周期状态截 Service 页整屏（需经 sudo 从 GUI 用户运行）
  -h, --help            Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release-dir) RELEASE_DIR="$2"; shift 2 ;;
    --evidence-dir) EVIDENCE_DIR="$2"; shift 2 ;;
    --ui-shots) UI_SHOTS=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
INSTALL_SCRIPT="${SCRIPT_DIR}/install-launchd-service.sh"
SHOT_SCRIPT="${SCRIPT_DIR}/capture-ui-service-page.sh"

# ---------- 预检 ----------
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "本 harness 只能在 macOS 上运行（实机或 CI macOS runner），当前: $(uname -s)" >&2
  exit 1
fi
if [[ $EUID -ne 0 ]]; then
  echo "Please run as root (sudo)." >&2
  exit 1
fi
if [[ -z "$RELEASE_DIR" ]]; then
  echo "Missing required --release-dir" >&2
  usage
  exit 1
fi
if [[ ! -f "${RELEASE_DIR}/PhotoPrivacyWorker" ]]; then
  echo "Missing executable in release dir: ${RELEASE_DIR}/PhotoPrivacyWorker" >&2
  echo "先运行 scripts/publish.sh --runtime <osx-rid> 产出发布目录。" >&2
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
  EVIDENCE_DIR="logs/launchd-verify"
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

probe_raw() {
  # launchctl list 的退出码与 stdout（0=已加载）
  set +e
  PROBE_OUT="$(launchctl list "$SERVICE_LABEL" 2>/dev/null)"
  PROBE_RC=$?
  set -e
}

get_pid() {
  # 从 launchctl list <label> 的 plist 形态输出提取 PID（无 PID 行则空）
  probe_raw
  if [[ $PROBE_RC -ne 0 ]]; then
    return 0
  fi
  printf '%s\n' "$PROBE_OUT" | sed -n 's/.*"PID"[[:space:]]*=[[:space:]]*\([0-9][0-9]*\).*/\1/p' | head -n 1
}

probe_state() {
  # 与 MacProbe::state 同判定：launchctl 退出码 + PID + plist 存在性
  probe_raw
  if [[ $PROBE_RC -ne 0 ]]; then
    if [[ -f "$PLIST_PATH" ]]; then
      echo "Stopped"
    else
      echo "NotInstalled"
    fi
    return 0
  fi
  local pid
  pid="$(printf '%s\n' "$PROBE_OUT" | sed -n 's/.*"PID"[[:space:]]*=[[:space:]]*\([0-9][0-9]*\).*/\1/p' | head -n 1)"
  if [[ -n "$pid" ]] && [[ "$pid" -gt 0 ]]; then
    echo "Running"
  else
    echo "Stopped"
  fi
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

launchd_stdio_present() {
  [[ -f "${AUDIT_FOLDER}/stdout.log" ]] || [[ -f "${AUDIT_FOLDER}/stderr.log" ]]
}

worker_absent() {
  ! pgrep -f "${WORKER_BIN}" >/dev/null 2>&1
}

run_with_timeout() {
  # run_with_timeout <秒> <输出文件> <命令...>：超时杀进程返回 124（macOS 无 GNU timeout）。
  # 不在函数内动 set -e：errexit 是全局态，函数内重开会让非零 return 把调用方打崩。
  local secs="$1" out="$2"
  shift 2
  "$@" > "$out" 2>&1 &
  local pid=$!
  local deadline=$(( $(date +%s) + secs ))
  while kill -0 "$pid" 2>/dev/null; do
    if [[ "$(date +%s)" -ge "$deadline" ]]; then
      kill -9 "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
      return 124
    fi
    sleep 1
  done
  local rc=0
  wait "$pid" || rc=$?
  return "$rc"
}

ui_probe_report() {
  # UI 层 Service 页同源探测（--service-probe-report）与 CLI probe_state 对拍：
  # 一致 = 「新 UI Service 页探测到服务状态」的机器可读证据（建窗前退出，无需图形会话）。
  local tag="$1"
  local report="$EVIDENCE_DIR/ui-probe-$tag.json"
  local out="$EVIDENCE_DIR/ui-probe-$tag.txt"
  rm -f "$report"
  local rc=0
  run_with_timeout 30 "$out" "${INSTALL_DIR}/PhotoPrivacy" \
    --service-probe-report "$report" \
    --worker "$EVIDENCE_DIR/no-worker-for-probe" || rc=$?
  if [[ ! -f "$report" ]]; then
    rec FAIL "UI Service 页探测报告未产出（${tag}，rc=${rc}，见 ui-probe-$tag.txt）"
    return 0
  fi
  local ui_state cli_state
  ui_state="$(sed -n 's/.*"state":"\([A-Za-z]*\)".*/\1/p' "$report")"
  cli_state="$(probe_state)"
  if [[ -n "$ui_state" ]] && [[ "$ui_state" == "$cli_state" ]]; then
    rec PASS "UI Service 页探测与 launchctl 判定一致（${tag}: ${ui_state}）"
  else
    rec FAIL "UI/CLI 探测分歧（${tag}: ui=${ui_state:-<空>} cli=${cli_state}）"
  fi
}

ui_shot() {
  # Service 页整屏截图（GUI 用户，--ui-shots 才启用）：内容供目检，不作像素断言。
  local tag="$1"
  if [[ "$UI_SHOTS" != 1 ]]; then
    return 0
  fi
  if [[ -z "${SUDO_USER:-}" ]] || [[ "$SUDO_USER" == "root" ]]; then
    rec FAIL "UI 截图 ${tag}：--ui-shots 需经 sudo 从 GUI 用户运行（SUDO_USER 缺失）"
    return 0
  fi
  if [[ ! -f "$SHOT_SCRIPT" ]]; then
    rec FAIL "UI 截图 ${tag}：截图脚本缺失（${SHOT_SCRIPT}）"
    return 0
  fi
  local uid out
  uid="$(id -u "$SUDO_USER")"
  out="$EVIDENCE_DIR/ui-shot-$tag.txt"
  # 证据目录是 root 建的：交还 GUI 用户可写，否则截图/配对状态落不了盘（首轮实测）
  chown "$SUDO_USER" "$EVIDENCE_DIR" 2>/dev/null || true
  local rc=0
  run_with_timeout 60 "$out" launchctl asuser "$uid" sudo -u "$SUDO_USER" \
    bash "$SHOT_SCRIPT" --install-dir "$INSTALL_DIR" --tag "$tag" --out-dir "$EVIDENCE_DIR" || rc=$?
  if [[ -s "$EVIDENCE_DIR/ui-$tag.png" ]]; then
    rec PASS "UI Service 页截图文件已产出（${tag}，内容待目检；无图形加速 VM 里 gpui 可能黑屏）：ui-$tag.png"
  else
    rec FAIL "UI Service 页截图未产出（${tag}，rc=${rc}，见 ui-shot-$tag.txt）"
  fi
}

new_pid_alive() {
  # 旧 PID 已死、launchd 已拉起新 PID
  local p
  p="$(get_pid)"
  if [[ -z "$p" ]] || [[ "$p" == "$OLD_PID" ]]; then
    return 1
  fi
  ps -p "$p" >/dev/null 2>&1
}

cleanup() {
  # 兜底：任一环节失败也不留加载中的服务（与产品卸载语义一致：unload + rm plist；
  # 对已卸载态幂等——失败一律吞掉）。
  launchctl unload -w "$PLIST_PATH" >/dev/null 2>&1 || true
  rm -f "$PLIST_PATH" >/dev/null 2>&1 || true
}
trap cleanup EXIT

# ---------- 0. 环境留证（虚拟环境/实机识别） ----------
capture 00-env.txt uname -a
capture 00-env.txt sw_vers
if [[ "$UI_SHOTS" != 1 ]]; then
  rec INFO "UI Service 页截图未启用（需要截图证据请传 --ui-shots）"
fi

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

# UI 层取证基线：安装前 = 未安装态（Service 页同源探测 + 截图）
ui_probe_report pre-install
ui_shot pre-install

# ---------- 2. 安装（install-launchd-service.sh，MacOps::install 同款通道） ----------
{
  echo "---- install 前 dscl 现状（#32 诊断） ----"
  echo '$ dscl . -list /Groups gid'
  dscl . -list /Groups gid 2>&1 || true
  echo '$ dscl . -read /Groups/'"${SERVICE_USER}"
  dscl . -read "/Groups/${SERVICE_USER}" 2>&1 || true
  echo '$ dscl . -read /Users/'"${SERVICE_USER}"
  dscl . -read "/Users/${SERVICE_USER}" 2>&1 || true
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
  --group "$SERVICE_GROUP" >> "${EVIDENCE_DIR}/02-install.txt" 2>&1
INSTALL_RC=$?
set -e
echo "[exit ${INSTALL_RC}]" >> "${EVIDENCE_DIR}/02-install.txt"
if [[ $INSTALL_RC -eq 0 ]]; then
  rec PASS "install-launchd-service.sh 执行成功"
else
  rec FAIL "install-launchd-service.sh 执行失败（exit ${INSTALL_RC}）"
fi

if [[ ! -f "$PLIST_PATH" ]]; then
  rec FAIL "plist 未写入 ${PLIST_PATH}——后续生命周期断言无意义，提前收口"
  echo "---- summary ----" | tee -a "$SUMMARY_FILE"
  echo "PASS=${PASS_COUNT} FAIL=${FAIL_COUNT}" | tee -a "$SUMMARY_FILE"
  exit 1
fi
rec PASS "plist 已写入 ${PLIST_PATH}"

# ---------- 3. 注册 + 常驻 ----------
if wait_until 30 state_is Running; then
  rec PASS "launchctl 注册且运行中（launchctl list ${SERVICE_LABEL} 出 PID）"
else
  rec FAIL "安装后 30s 内未进入 Running（probe=$(probe_state)）"
fi
capture 03-launchctl-after-install.txt launchctl print "system/${SERVICE_LABEL}"
capture 03-launchctl-after-install.txt launchctl list "$SERVICE_LABEL"

OLD_PID="$(get_pid)"
if [[ -n "$OLD_PID" ]] && ps -p "$OLD_PID" >/dev/null 2>&1; then
  rec PASS "Worker 进程存活（pid=${OLD_PID}）"
else
  rec FAIL "取不到存活的 Worker 进程（pid='${OLD_PID}'）"
fi

# 稳定性：5s 后 PID 不变才不是崩溃重启循环（KeepAlive 会把崩溃循环伪装成 Running）
sleep 5
STABLE_PID="$(get_pid)"
if [[ -n "$OLD_PID" ]] && [[ "$STABLE_PID" == "$OLD_PID" ]] && ps -p "$STABLE_PID" >/dev/null 2>&1; then
  rec PASS "Worker 进程稳定（5s 内 PID 不变=${STABLE_PID}）"
else
  rec FAIL "Worker 进程不稳：5s 内 PID ${OLD_PID}→${STABLE_PID}（疑似启动后崩溃循环）"
fi
OLD_PID="$STABLE_PID"

# UI 层取证：运行中态（Service 页同源探测 + 截图），在 KeepAlive 试验前取稳定态
ui_probe_report running
ui_shot running

# KeepAlive 常驻（SuccessfulExit=false）：SIGKILL 后 launchd 应拉起新进程
if [[ -n "$OLD_PID" ]]; then
  kill -9 "$OLD_PID" >> "${EVIDENCE_DIR}/04-keepalive.txt" 2>&1 || true
  echo "killed pid=${OLD_PID} at $(date '+%H:%M:%S')" >> "${EVIDENCE_DIR}/04-keepalive.txt"
  if wait_until 45 new_pid_alive; then
    NEW_PID="$(get_pid)"
    rec PASS "KeepAlive 常驻：SIGKILL 后 launchd 重启（${OLD_PID} → ${NEW_PID}）"
  else
    rec FAIL "SIGKILL 后 45s 内 launchd 未重启（probe=$(probe_state)）"
  fi
else
  rec FAIL "KeepAlive 重启未测：无可杀的 Worker 进程"
fi
capture 04-keepalive.txt launchctl list "$SERVICE_LABEL"

# ---------- 4. 日志 ----------
if wait_until 30 launchd_stdio_present; then
  rec PASS "launchd 标准日志已创建（${AUDIT_FOLDER}/stdout.log 或 stderr.log）"
else
  rec FAIL "30s 内未见 launchd 标准日志（stdout.log/stderr.log）"
fi
if wait_until 30 worker_logs_present; then
  rec PASS "Worker Serilog 日志已落盘（${WORKER_LOG_GLOB}）"
else
  rec FAIL "30s 内未见 Worker Serilog 日志（${WORKER_LOG_GLOB}）"
fi
capture 05-logs.txt ls -la "$AUDIT_FOLDER"
# shellcheck disable=SC2086
capture 05-logs.txt ls -la ${INSTALL_DIR}/logs
{
  echo "---- launchd stdout/stderr ----"
  tail -n 20 "${AUDIT_FOLDER}/stdout.log" 2>/dev/null || true
  tail -n 20 "${AUDIT_FOLDER}/stderr.log" 2>/dev/null || true
  echo "---- worker serilog ----"
  # shellcheck disable=SC2086
  tail -n 20 $WORKER_LOG_GLOB 2>/dev/null || true
} >> "${EVIDENCE_DIR}/05-logs.txt"
if grep -q "no watch folder is configured" ${WORKER_LOG_GLOB} 2>/dev/null; then
  rec INFO "Worker 启动待机行在列（no watch folder is configured）"
fi

# 服务模式 UDS 端点（硬断言：worker 绑定失败会在 Serilog 记 bind error，服务页数据源将不可用）
capture 06-socket.txt ls -la "${INSTALL_DIR}/Library/Application Support/photoprivacy"
if wait_until 30 test -S "$SERVICE_SOCKET"; then
  rec PASS "服务模式 UDS 端点已绑定（${SERVICE_SOCKET}）"
else
  capture 06-socket.txt find "$INSTALL_DIR" \( -name "*.sock" -o -name "*.log" \)
  # 诊断 HOME 解析分歧：.NET UserProfile 取 HOME/或 passwd home，落点不同 socket 路径就不同
  CUR_PID="$(get_pid)"
  if [[ -n "$CUR_PID" ]]; then
    capture 06-socket.txt ps eww -p "$CUR_PID"
  fi
  rec FAIL "30s 内未见服务模式 UDS 端点（${SERVICE_SOCKET}）"
fi

# ---------- 5. 停止（MacOps::stop 同款命令串） ----------
set +e
launchctl unload -w "$PLIST_PATH" > "${EVIDENCE_DIR}/07-stop.txt" 2>&1
STOP_RC=$?
set -e
echo "[exit ${STOP_RC}]" >> "${EVIDENCE_DIR}/07-stop.txt"
if [[ $STOP_RC -eq 0 ]]; then
  rec PASS "launchctl unload 停止成功"
else
  rec FAIL "launchctl unload 停止失败（exit ${STOP_RC}）"
fi
expect_state Stopped "停止后状态=已安装未运行（plist 仍在、不再加载）"

# UI 层取证：已停止态
ui_probe_report stopped
ui_shot stopped

# ---------- 6. 启动（MacOps::start 同款命令串） ----------
set +e
launchctl load -w "$PLIST_PATH" > "${EVIDENCE_DIR}/08-start.txt" 2>&1
START_RC=$?
set -e
echo "[exit ${START_RC}]" >> "${EVIDENCE_DIR}/08-start.txt"
if [[ $START_RC -eq 0 ]]; then
  rec PASS "launchctl load 启动成功"
else
  rec FAIL "launchctl load 启动失败（exit ${START_RC}）"
fi
if wait_until 30 state_is Running; then
  rec PASS "再次启动恢复运行中"
else
  rec FAIL "启动后 30s 内未进入 Running（probe=$(probe_state)）"
fi

# ---------- 7. 卸载（MacOps::uninstall 同款命令串，退出码透传语义一致） ----------
set +e
bash -c 'launchctl unload -w "$1" 2>/dev/null; unload_rc=$?; rm -f "$1"; exit $unload_rc' \
  uninstall "$PLIST_PATH" > "${EVIDENCE_DIR}/09-uninstall.txt" 2>&1
UNINSTALL_RC=$?
set -e
echo "[exit ${UNINSTALL_RC}]" >> "${EVIDENCE_DIR}/09-uninstall.txt"
if [[ $UNINSTALL_RC -eq 0 ]]; then
  rec PASS "卸载命令成功（unload 退出码透传）"
else
  rec FAIL "卸载命令失败（exit ${UNINSTALL_RC}）"
fi
expect_state NotInstalled "卸载后状态=未安装"
if [[ ! -f "$PLIST_PATH" ]]; then
  rec PASS "plist 已删除"
else
  rec FAIL "plist 仍存在：${PLIST_PATH}"
fi
if wait_until 15 worker_absent; then
  rec PASS "卸载后无 Worker 进程残留"
else
  capture 10-final.txt pgrep -fl PhotoPrivacyWorker
  rec FAIL "卸载 15s 后仍有 Worker 进程残留"
fi

# UI 层取证：卸载后 = 未安装态
ui_probe_report uninstalled
ui_shot uninstalled

# ---------- 8. 收口 ----------
capture 10-final.txt launchctl list "$SERVICE_LABEL"
capture 10-final.txt launchctl print "system/${SERVICE_LABEL}"
{
  echo "---- 结论 ----"
  echo "PASS=${PASS_COUNT} FAIL=${FAIL_COUNT}"
} >> "${SUMMARY_FILE}"
echo "---- summary ----"
cat "$SUMMARY_FILE"

if [[ "$FAIL_COUNT" -gt 0 ]]; then
  exit 1
fi
exit 0
