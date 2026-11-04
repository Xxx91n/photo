#!/usr/bin/env bash
# verify-launchd-service.sh —— #32 macOS launchd 安装/卸载/日志生命周期验证 harness。
#
# 用途：在 macOS（实机或 CI macOS runner 虚拟环境）对 launchd 服务安装链做端到端断言：
#   安装 → 注册 → 常驻（KeepAlive 重启）→ 日志 → 停止 → 启动 → 卸载 → 清理。
# 操作与判定口径对齐 ui/crates/photoprivacy-service/src/unix.rs：
#   - 安装走 scripts/install-launchd-service.sh（MacOps::install 同款，仅跳过 osascript 提权包装——
#     本 harness 本身要求 root 运行）；
#   - 停止/启动/卸载用 MacOps 同款 launchctl 命令串；
#   - 状态判定同 MacProbe::state + parse_launchctl_print：launchctl print system/<label> 退出码 + PID 行
#     + plist 存在性（plist 在而未加载 = 已安装未运行，不是未安装）。显式系统域查询——legacy
#     `launchctl list <label>` 按调用者域查找，GUI 用户查不到系统 LaunchDaemon（#172）。
# 因此脚本结论可直接代表新 UI Service 页探测/操作语义；UI 层证据两路产出：
#   - 机器可读：PhotoPrivacy --service-probe-report 与 CLI probe_state 对拍（硬断言，UI/CLI 一致）。
#     探测报告以 GUI 用户语境运行（与截图同语境）——对拍结果才代表真实 Service 页行为（#172）；
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
# socket 访问组独立于数据组：GUI 用户只进这个组，拿不到 hot/audit/quarantine 的组写权限
# （评审 CWE-732：同组可绕过 IPC 直接删审计与隔离文件）。
SOCKET_GROUP="photoprivacy-ipc"
WORKER_BIN="${INSTALL_DIR}/PhotoPrivacyWorker"
# Worker Serilog 落点：相对 CWD 的 logs/worker-.log（Program.cs ResolveLogFilePath 非 Windows 分支），
# launchd WorkingDirectory=INSTALL_DIR（install 脚本写入的 plist）→ INSTALL_DIR/logs/worker-*.log。
WORKER_LOG_GLOB="${INSTALL_DIR}/logs/worker-*.log"
# 服务模式 UDS 端点（WorkerIpcEndpointNames.MacServiceSocketPath）：系统级固定落点，
# 与调用者身份无关（#167）。不再随服务用户 home（NFSHomeDirectory=INSTALL_DIR）漂移。
SERVICE_SOCKET="/Library/Application Support/photoprivacy/worker.sock"
SERVICE_SOCKET_DIR="/Library/Application Support/photoprivacy"

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

# ---------- GUI 用户（安装脚本 --gui-user 的显式语义） ----------
# harness 要求 sudo 从 GUI 用户运行，SUDO_USER 即调用者；这里显式传给安装脚本，让 --gui-user 的显式
# 分支（指定用户不存在/加不进组必须让安装失败）在 CI 里真的被走到，而不是全程只靠 SUDO_USER 推断兜底
# （评审 nit：安装调用漏传 --gui-user，显式语义零覆盖）。
GUI_USER="${SUDO_USER:-}"
INSTALL_GUI_ARGS=()
if [[ -n "$GUI_USER" ]]; then
  INSTALL_GUI_ARGS=(--gui-user "$GUI_USER")
fi

# 显式空 --gui-user 必须硬失败（评审 R5 回归锚点）：早前它会静默落进「未解析到 GUI 用户」的告警分支
# 并以 exit 0 收场，调用方会把「没给名字」误判成「IPC 权限已供给完成」。安装脚本在参数解析阶段就拒绝，
# 早于任何账户/目录副作用，因此可以在铺发布包之前安全地跑这条负向自证。
if bash "$INSTALL_SCRIPT" --gui-user "" >/dev/null 2>&1; then
  rec FAIL "install-launchd-service.sh 接受了空 --gui-user（期望非零退出）"
else
  rec PASS "空 --gui-user 被拒绝（显式语义不再静默降级成告警+成功）"
fi

probe_raw() {
  # launchctl print system/<label> 的退出码与 stdout（显式系统域；0=系统域已加载，#172）
  set +e
  PROBE_OUT="$(launchctl print "system/${SERVICE_LABEL}" 2>/dev/null)"
  PROBE_RC=$?
  set -e
}

extract_pid() {
  # 从 print 输出提取 PID 行（新式 `pid = N`，兼容旧式 `"PID" = N;` 的引号；无 PID 行则空）
  printf '%s\n' "$1" | sed -n 's/.*[Pp][Ii][Dd]"*[[:space:]]*=[[:space:]]*\([0-9][0-9]*\).*/\1/p' | head -n 1
}

group_primary_gid() {
  # 组记录的 PrimaryGroupID；缺失或不可读时输出空串。`|| true` 是必须的：`set -euo pipefail`
  # 下 dscl 非零会直接终止 harness，后面的 FAIL 汇总与停止/卸载都跑不到。
  local name="$1" gid
  gid="$(dscl . -read "/Groups/${name}" PrimaryGroupID 2>/dev/null \
    | awk 'NR==1 && $2 ~ /^[0-9]+$/ {print $2}')" || true
  printf '%s\n' "$gid"
}

get_pid() {
  # 服务进程 PID（print 输出无 PID 行则空）
  probe_raw
  if [[ $PROBE_RC -ne 0 ]]; then
    return 0
  fi
  extract_pid "$PROBE_OUT"
}

probe_state() {
  # 与 MacProbe::state 同判定：print 退出码 + PID + plist 存在性
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
  pid="$(extract_pid "$PROBE_OUT")"
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
  # 探测以 GUI 用户语境运行（launchctl asuser + sudo -u，与截图同语境）——对拍结果才代表
  # 真实 Service 页行为；root 语境探测会掩盖 launchctl 域语义缺陷（#172）。
  # 第二参数 expect_reachable=1 时要求 IPC 真的可达（仅服务应运行阶段）。
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
  local uid user_dir report out rc
  uid="$(id -u "$SUDO_USER")"
  # GUI 用户只拿专用子目录可写，root 侧证据文件不交出去（CWE-59，与 ui_shot 同口径）；
  # 报告由 root 收回证据根（rename 不跟随符号链接，无截断面）。
  user_dir="$EVIDENCE_DIR/ui-user"
  mkdir -p "$user_dir"
  chown "$SUDO_USER" "$user_dir" 2>/dev/null || true
  report="$user_dir/ui-probe-$tag.json"
  out="$EVIDENCE_DIR/ui-probe-$tag.txt"
  rm -f "$report"
  rc=0
  run_with_timeout 30 "$out" launchctl asuser "$uid" sudo -u "$SUDO_USER" \
    "${INSTALL_DIR}/PhotoPrivacy" \
    --service-probe-report "$report" \
    --service-probe-ipc \
    --worker "$user_dir/no-worker-for-probe" || rc=$?
  if [[ -f "$report" ]]; then
    mv -f "$report" "$EVIDENCE_DIR/ui-probe-$tag.json"
    report="$EVIDENCE_DIR/ui-probe-$tag.json"
  fi
  # 命令失败/超时（rc!=0）时报告不作数：异常退出/超时杀（rc=124）后的残留报告
  # 即使文件在、状态对拍一致也不得洗成 PASS（CodeRabbit #175 审查意见，fail-closed）。
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
    rec PASS "UI Service 页探测与 launchctl 判定一致（${tag}: ${ui_state}，GUI 用户语境）"
  else
    rec FAIL "UI/CLI 探测分歧（${tag}: ui=${ui_state:-<空>} cli=${cli_state}）"
  fi
  # #167：服务模式 UDS 落点必须与调用者身份无关——无论服务在跑还是已停，GUI 用户语境的
  # 客户端都要解析到安装脚本建的那个系统级 socket，而不是自己 home 下的幽灵路径。
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

ui_shot() {
  # Service 页整屏截图（GUI 用户，--ui-shots 才启用）：内容供目检，不作像素断言。
  local tag="$1"
  if [[ "$UI_SHOTS" != 1 ]]; then
    return 0
  fi
  if [[ -z "$GUI_USER" || "$GUI_USER" == "root" ]]; then
    rec FAIL "UI 截图 ${tag}：--ui-shots 需经 sudo 从 GUI 用户运行（GUI_USER 缺失或为 root）"
    return 0
  fi
  if [[ ! -f "$SHOT_SCRIPT" ]]; then
    rec FAIL "UI 截图 ${tag}：截图脚本缺失（${SHOT_SCRIPT}）"
    return 0
  fi
  local uid out shot_dir ui_config ui_config_dir
  local config_args
  config_args=()
  uid="$(id -u "$GUI_USER")"
  out="$EVIDENCE_DIR/ui-shot-$tag.txt"
  # 截图只给 GUI 用户开一个专用子目录可写：root 侧证据文件不交出去，消除符号链接
  # 截断攻击面（CodeRabbit 审查意见，CWE-59）
  shot_dir="$EVIDENCE_DIR/ui-shots"
  mkdir -p "$shot_dir"
  chown "$GUI_USER" "$shot_dir" 2>/dev/null || true
  # 安装后 config.json 是 0750/0640 服务账户:数据组，GUI 用户读不到（#167 配置写入边界）。
  # 截图脚本需要一份配置副本打开 #174 的静默开关：root 侧复制到 root 属主目录（GUI 不可写，
  # 避免在 GUI 可写目录里被预置符号链接做 CWE-59 截断），文件本身归 GUI 只读，不为了取证
  # 反过来放宽安装目录 / 配置权限。
  if [[ -f "$CONFIG_PATH" ]]; then
    ui_config_dir="$EVIDENCE_DIR/ui-config"
    mkdir -p "$ui_config_dir"
    chmod 0755 "$ui_config_dir"
    ui_config="$ui_config_dir/ui-config-$tag.json"
    rm -f "$ui_config"
    cp "$CONFIG_PATH" "$ui_config"
    chown "$GUI_USER" "$ui_config" 2>/dev/null || true
    chmod 0600 "$ui_config"
    config_args=(--config "$ui_config")
  fi
  local rc=0
  run_with_timeout 60 "$out" launchctl asuser "$uid" sudo -u "$GUI_USER" \
    bash "$SHOT_SCRIPT" --install-dir "$INSTALL_DIR" --tag "$tag" --out-dir "$shot_dir" \
    ${config_args[@]+"${config_args[@]}"} || rc=$?
  if [[ $rc -eq 0 ]] && [[ -s "$shot_dir/ui-$tag.png" ]]; then
    rec PASS "UI Service 页截图文件已产出（${tag}，内容待目检；无图形加速 VM 里 gpui 可能黑屏）：ui-shots/ui-$tag.png"
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
  --group "$SERVICE_GROUP" \
  --socket-group "$SOCKET_GROUP" \
  ${INSTALL_GUI_ARGS[@]+"${INSTALL_GUI_ARGS[@]}"} >> "${EVIDENCE_DIR}/02-install.txt" 2>&1
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
ui_probe_report running 1
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
capture 06-socket.txt ls -la "$SERVICE_SOCKET_DIR"
if wait_until 30 test -S "$SERVICE_SOCKET"; then
  rec PASS "服务模式 UDS 端点已绑定（${SERVICE_SOCKET}）"
  # #167：组边界必须真的落在盘上——socket 0660 + 服务用户/socket 访问组，目录 0750。
  # 权限偏了（umask 收成 0600、目录被改成 0755）意味着 GUI 用户要么连不上、要么谁都能连；
  # 这里按安装脚本与 UnixDomainSocketIpcTransport 的承诺逐项对拍，而不是只看 socket 在不在。
  SOCKET_MODE="$(stat -f '%Lp' "$SERVICE_SOCKET" 2>/dev/null)"
  SOCKET_OWNER="$(stat -f '%Su:%Sg' "$SERVICE_SOCKET" 2>/dev/null)"
  SOCKET_DIR_MODE="$(stat -f '%Lp' "$SERVICE_SOCKET_DIR" 2>/dev/null)"
  if [[ "$SOCKET_MODE" == "660" ]] \
    && [[ "$SOCKET_OWNER" == "${SERVICE_USER}:${SOCKET_GROUP}" ]] \
    && [[ "$SOCKET_DIR_MODE" == "750" ]]; then
    rec PASS "服务模式 UDS 权限符合组边界（socket ${SOCKET_MODE} ${SOCKET_OWNER}，目录 ${SOCKET_DIR_MODE}）"
  else
    rec FAIL "服务模式 UDS 权限不符（socket=${SOCKET_MODE:-<空>} ${SOCKET_OWNER:-<空>}，目录=${SOCKET_DIR_MODE:-<空>}；期望 660 ${SERVICE_USER}:${SOCKET_GROUP} / 750）"
  fi

  # 评审 CWE-732：审计目录给 GUI 用户的只能是「组读」——能看日志页，不能写、不能删。
  AUDIT_MODE="$(stat -f '%Lp' "$AUDIT_FOLDER" 2>/dev/null)"
  AUDIT_GROUP="$(stat -f '%Sg' "$AUDIT_FOLDER" 2>/dev/null)"
  # setgid 位以符号串为准：部分 BSD 实现的 %Lp 只回三位（750），%Sp 的 group 执行位会显示 s/S。
  AUDIT_SYM="$(stat -f '%Sp' "$AUDIT_FOLDER" 2>/dev/null)"
  AUDIT_RAW="$(stat -f '%p' "$AUDIT_FOLDER" 2>/dev/null)"
  AUDIT_SGID="${AUDIT_SYM:6:1}"
  if [[ "$AUDIT_GROUP" == "$SOCKET_GROUP" ]] \
    && [[ "$AUDIT_SGID" == "s" || "$AUDIT_SGID" == "S" ]] \
    && [[ "$AUDIT_MODE" == "2750" || "$AUDIT_MODE" == "750" ]]; then
    rec PASS "审计目录组边界正确（${AUDIT_FOLDER} mode=${AUDIT_MODE} raw=${AUDIT_RAW} ${AUDIT_SYM} ${AUDIT_GROUP}：组只读，无组写）"
  else
    rec FAIL "审计目录组边界不符（${AUDIT_FOLDER} mode=${AUDIT_MODE:-<空>} raw=${AUDIT_RAW:-<空>} sym=${AUDIT_SYM:-<空>} ${AUDIT_GROUP:-<空>}；期望 2750 ${SOCKET_GROUP} 的组只读视图）"
  fi

  # 评审 R1：安装目录与 Worker 日志此前没有任何 chmod，发布包按 umask 落在 0755，本地任意用户能
  # 遍历读走 config 与 logs/worker-*.log（请求 URL、文件名、错误栈）。安装脚本承诺 0755 的安装目录
  # （GUI 用户要执行其中的 UI 二进制：0750 下实测 sudo 报 Permission denied，收敛不能把 UI 一起收掉）
  # + 敏感面 config/ 750/640、logs/ 2750/640，这里逐项对拍。
  INSTALL_MODE="$(stat -f '%Lp' "$INSTALL_DIR" 2>/dev/null)"
  WLOG_DIR_MODE="$(stat -f '%Lp' "${INSTALL_DIR}/logs" 2>/dev/null)"
  # shellcheck disable=SC2086
  WLOG_MODE="$(stat -f '%Lp' ${WORKER_LOG_GLOB} 2>/dev/null | head -n 1)"
  CFG_DIR_MODE="$(stat -f '%Lp' "${INSTALL_DIR}/config" 2>/dev/null)"
  CFG_FILE="$(find "${INSTALL_DIR}/config" -maxdepth 1 -type f 2>/dev/null | head -n 1)"
  CFG_FILE_MODE=""
  [[ -n "$CFG_FILE" ]] && CFG_FILE_MODE="$(stat -f '%Lp' "$CFG_FILE" 2>/dev/null)"
  if [[ "$INSTALL_MODE" == "755" ]] \
    && [[ "$WLOG_DIR_MODE" == "2750" || "$WLOG_DIR_MODE" == "750" ]] \
    && [[ "$WLOG_MODE" == "640" ]] \
    && [[ -z "$CFG_DIR_MODE" || "$CFG_DIR_MODE" == "750" ]] \
    && [[ -z "$CFG_FILE_MODE" || "$CFG_FILE_MODE" == "640" ]]; then
    rec PASS "安装目录权限收敛（install ${INSTALL_MODE}，config ${CFG_DIR_MODE:-<无>}/${CFG_FILE_MODE:-<无>}，logs ${WLOG_DIR_MODE}，日志文件 ${WLOG_MODE}）"
  else
    rec FAIL "安装目录/日志权限未收敛（install=${INSTALL_MODE:-<空>} config=${CFG_DIR_MODE:-<无>}/${CFG_FILE_MODE:-<无>} logs=${WLOG_DIR_MODE:-<空>} file=${WLOG_MODE:-<空>}；期望 755 / 750+640 / 2750 / 640）"
  fi

  # R1 的功能面 + GUI 组边界：GUI_USER 是 `SUDO_USER` 的受控投影（`set -u` 下裸 `$SUDO_USER`
  # 会直接终止脚本）。root 直登或 `SUDO_USER=root` 无法代表真实 GUI 用户，与 ui_probe_report
  # 同口径记 FAIL，而不是静默跳过——否则汇总会在「组边界根本没验」时仍显示全绿（fail-closed）。
  if [[ -z "$GUI_USER" || "$GUI_USER" == "root" ]]; then
    rec FAIL "GUI 组边界未验证：需经 sudo 从 GUI 用户运行（GUI_USER 缺失或为 root）"
  else
    if sudo -u "$GUI_USER" test -x "${INSTALL_DIR}/PhotoPrivacy"; then
      rec PASS "GUI 用户 ${GUI_USER} 可执行安装目录内的 UI 二进制（${INSTALL_DIR}/PhotoPrivacy）"
    else
      rec FAIL "GUI 用户 ${GUI_USER} 无法执行 ${INSTALL_DIR}/PhotoPrivacy——安装目录收紧过头，UI 无法启动"
    fi
    # GUI 用户只应进 socket 访问组：进了数据组就等于拿到 2770 的组写权限，可绕过 IPC 删审计/隔离文件。
    if dseditgroup -o checkmember -m "$GUI_USER" "$SOCKET_GROUP" >/dev/null 2>&1; then
      rec PASS "GUI 用户 ${GUI_USER} 已在 socket 访问组 ${SOCKET_GROUP}"
    else
      rec FAIL "GUI 用户 ${GUI_USER} 不在 socket 访问组 ${SOCKET_GROUP}——服务模式 UI 连不上端点"
    fi
    if dseditgroup -o checkmember -m "$GUI_USER" "$SERVICE_GROUP" >/dev/null 2>&1; then
      rec FAIL "GUI 用户 ${GUI_USER} 仍在数据组 ${SERVICE_GROUP}——可绕过 IPC 直接删审计/隔离文件"
    else
      rec PASS "GUI 用户 ${GUI_USER} 未获授数据组 ${SERVICE_GROUP}（无组写）"
    fi
  fi

  # 组隔离的第二道事实：socket 访问组与数据组必须是两条不同 GID 的组记录。
  # 同名/同 GID 的崩坏形态会让 GUI 用户虽然只进了 socket 组，却继承数据组对 hot/quarantine 的 2770
  # 组写（#180 评审 Major：安装器早期把数据组 GID 复用给 socket 组）。这里按 dscl 记录对拍，
  # 不依赖安装器自述。放在「服务已起来」分支内：安装失败时早已提前收口，不产出脱离上下文的 PASS。
  SOCKET_GID="$(group_primary_gid "$SOCKET_GROUP")"
  SERVICE_GID="$(group_primary_gid "$SERVICE_GROUP")"
  if [[ -n "$SOCKET_GID" && -n "$SERVICE_GID" \
    && "$SOCKET_GID" != "$SERVICE_GID" ]]; then
    rec PASS "socket 访问组 GID 独立于数据组（${SOCKET_GROUP}=${SOCKET_GID} ≠ ${SERVICE_GROUP}=${SERVICE_GID}）"
  else
    rec FAIL "socket 访问组 GID 未与数据组分离（${SOCKET_GROUP}=${SOCKET_GID:-<空>}，${SERVICE_GROUP}=${SERVICE_GID:-<空>}）——同 GID 等于同组权限"
  fi
else
  capture 06-socket.txt find "$SERVICE_SOCKET_DIR" "${INSTALL_DIR}" \( -name "*.sock" -o -name "*.log" \)
  # 诊断 HOME 解析分歧：落点若仍停在服务用户 home 下，这里会直接列出那个幽灵 socket。
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
