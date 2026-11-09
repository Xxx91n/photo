#!/usr/bin/env bash
# capture-ui-service-page.sh —— #32 UI Service 页截图留证据（以 GUI 用户运行）。
#
# 以 --open-page service 启动发布包里的 PhotoPrivacy，等首屏渲染后整屏截图，
# 连同时刻的 launchctl 状态一并留证据，然后退出 UI。供 verify-launchd-service.sh
# 在各生命周期状态下调用（虚拟环境 = CI macOS runner；也可在实机手动执行）。
#
# #174：`--open-page` 显式覆盖静默启动（ui/crates/photoprivacy-ui/src/main.rs 的
# should_open_main_window），ui.hide_main_window_on_startup=true 的机器上首实例也直接建窗。
# 注意下面的第二次实例唤起只是兜「已有实例先跑起来」的单实例检查，它对 #174 的回归不敏感
# （静默被吞时第二实例照样能把窗带出来）——不能拿它当本票的判定依据。
#
# 用法: ./capture-ui-service-page.sh --install-dir <dir> --tag <name> --out-dir <dir>
#   --install-dir  已解压发布包的安装目录（含 PhotoPrivacy 与 config/config.json）
#   --tag          证据标签（如 running / stopped）
#   --out-dir      证据落盘目录
#
# 截图内容（页面呈现）不作机器断言，产物供目检；机器可读的 UI 层探测证据由
# PhotoPrivacy --service-probe-report 另行产出（verify-launchd-service.sh 负责对拍）。
# 兼容 macOS 自带 bash 3.2。
set -euo pipefail

INSTALL_DIR=""
TAG=""
OUT_DIR=""

usage() {
  cat <<EOF
Usage: $0 --install-dir <dir> --tag <name> --out-dir <dir>

Options:
  --install-dir <dir>  已解压发布包的安装目录（含 PhotoPrivacy）
  --tag <name>         证据标签（如 running / stopped）
  --out-dir <dir>      证据落盘目录
  -h, --help           Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    --tag) TAG="$2"; shift 2 ;;
    --out-dir) OUT_DIR="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -z "$INSTALL_DIR" ]] || [[ -z "$TAG" ]] || [[ -z "$OUT_DIR" ]]; then
  echo "Missing required arguments" >&2
  usage
  exit 1
fi

UI_BIN="${INSTALL_DIR}/PhotoPrivacy"
if [[ ! -x "$UI_BIN" ]]; then
  echo "UI binary not found: ${UI_BIN}" >&2
  exit 1
fi

mkdir -p "$OUT_DIR"
PNG="${OUT_DIR}/ui-${TAG}.png"
LAUNCHCTL_TXT="${OUT_DIR}/ui-${TAG}-launchctl.txt"
# 清掉旧产物：同目录重跑时残留 PNG 会让上层“文件非空”断言假绿（CodeRabbit 审查意见）
rm -f "$PNG"

# #174：本脚本要证明的是「静默启动配置下 --open-page 仍能建窗」。发布包的 config 来自
# config.sample.json（hide_main_window_on_startup: false），不显式打开这个开关，这条路径
# 在 CI 上永远测不到。所以用配置副本打开它——安装目录里的 config.json 一字不动。
# 安装后 config.json 对 GUI 用户不可读（0750/0640，服务账户:数据组，#167 配置写入边界）；
# 此时不复制受保护配置、也不放宽安装目录权限，改用最小默认配置（各节 serde default）只打开
# 静默开关。UI 探测腿在加载配置前即返回，同样不依赖安装目录配置。
UI_CONFIG="${OUT_DIR}/ui-config-${TAG}.json"
if [[ -r "${INSTALL_DIR}/config/config.json" ]]; then
  cp "${INSTALL_DIR}/config/config.json" "$UI_CONFIG"
else
  echo "安装目录 config.json 对 GUI 不可读（预期：服务安装后 0750/0640）；截图改用最小默认配置" >&2
  printf '{\n  "ui": {\n    "hide_main_window_on_startup": true\n  }\n}\n' > "$UI_CONFIG"
fi
SILENT_KEY='hide_main_window_on_startup'
if [[ "$(grep -c "\"${SILENT_KEY}\"" "$UI_CONFIG")" -ne 1 ]]; then
  echo "配置副本里找不到唯一的 ${SILENT_KEY}，无法构造静默启动场景（#174）: ${UI_CONFIG}" >&2
  exit 1
fi
sed -E "s/(\"${SILENT_KEY}\"[[:space:]]*:[[:space:]]*)false/\1true/" "$UI_CONFIG" > "${UI_CONFIG}.tmp"
mv "${UI_CONFIG}.tmp" "$UI_CONFIG"
if ! grep -Eq "\"${SILENT_KEY}\"[[:space:]]*:[[:space:]]*true" "$UI_CONFIG"; then
  echo "把 ${SILENT_KEY} 置 true 失败，静默启动场景未成立（#174）: ${UI_CONFIG}" >&2
  exit 1
fi

# 起跑前不能有已在跑的 UI：单实例检查会让本脚本借到「没加载配置副本」的旧实例，
# 那样静默场景根本没成立（CodeRabbit 审查意见）。
if pgrep -x PhotoPrivacy > /dev/null 2>&1; then
  echo "已有 PhotoPrivacy 实例在跑，先退出再截图留证据（#174 要求全新进程）:" >&2
  pgrep -fl PhotoPrivacy >&2 || true
  exit 1
fi

# 首屏渲染余量：服务状态在建窗前已同步探测（main.rs 首拍），截图等窗口稳定。
STARTUP_WAIT=6
# 读得到 osascript 的四元组、且宽高为正才算建了窗；无窗时报 `Invalid index (-1719)`，
# 0×0 的退化窗口也不算。
ui_window_geometry() {
  grep -Eq '^[0-9-]+,[[:space:]]*[0-9-]+,[[:space:]]*[1-9][0-9]*,[[:space:]]*[1-9][0-9]*' "$1"
}

# 静默启动只在「托盘会建」时才生效：`tray::should_show_tray_icon` 在服务已安装时返回 false，
# `should_start_hidden` 随之恒 false，窗口无论如何都会出。所以服务已安装的 tag（running/stopped）
# 只取截图，不负责 #174 的判断——否则对照组必然建窗，把正常行为判成失败。
SERVICE_PLIST="/Library/LaunchDaemons/com.photoprivacy.cleaner.plist"
SILENT_APPLICABLE=1
if [[ -e "$SERVICE_PLIST" ]]; then
  SILENT_APPLICABLE=0
  echo "服务已安装（${SERVICE_PLIST} 存在）→ 托盘不建、静默不生效：本 tag 只取截图，不判 #174"
fi

# 对照组（CodeRabbit 审查意见）：同一静默配置、不带 --open-page 必须无窗。托盘创建失败时
# `should_start_hidden` 恒 false，这一组会建窗——那说明静默根本没生效，正式组即使出窗也不算数。
if [[ "$SILENT_APPLICABLE" -eq 1 ]]; then
  CONTROL_LOG="${OUT_DIR}/ui-${TAG}-control.log"
  CONTROL_WINDOW="${OUT_DIR}/ui-${TAG}-control-window.txt"
  "$UI_BIN" --config "$UI_CONFIG" --worker "${OUT_DIR}/no-worker-for-shot" > "$CONTROL_LOG" 2>&1 &
  CONTROL_PID=$!
  sleep "$STARTUP_WAIT"
  # 进程必须还活着：早退的对照组拿不到窗口，那是「崩溃」不是「静默生效」（CodeRabbit 审查意见）。
  if ! kill -0 "$CONTROL_PID" 2>/dev/null; then
    echo "对照组进程已退出（未驻守），静默条件无法判定（#174），日志: ${CONTROL_LOG}" >&2
    cat "$CONTROL_LOG" >&2 || true
    exit 1
  fi
  osascript -e 'tell application "System Events" to tell process "PhotoPrivacy" to get {position, size} of window 1' > "$CONTROL_WINDOW" 2>&1 || true
  kill "$CONTROL_PID" 2>/dev/null || true
  wait "$CONTROL_PID" 2>/dev/null || true
  if ui_window_geometry "$CONTROL_WINDOW"; then
    echo "静默配置下无 --open-page 仍建窗：静默启动未生效，正式组结果不可采信（#174）: ${CONTROL_WINDOW}" >&2
    cat "$CONTROL_WINDOW" >&2
    exit 1
  fi
  # 「无窗」只认 System Events 的 Invalid index（进程在、窗口不存在）。权限或其它错误
  # （-1743 等）说明这次截图根本没跑通，不能当成静默生效的证据。
  if ! grep -q "Invalid index" "$CONTROL_WINDOW"; then
    echo "对照组没给出『无窗』的确定结论（osascript 报错不是 Invalid index，像权限/会话问题）: ${CONTROL_WINDOW}" >&2
    cat "$CONTROL_WINDOW" >&2
    exit 1
  fi
  # 让单实例检查完全释放，再起正式实例。
  sleep 2
fi

# --worker 指向不存在路径：截图场景只看 Service 页探测，不拉起后台 Worker
# （避免与服务 Worker 同 config 并存的单写者让位场景混入，#162）。
UI_LOG="${OUT_DIR}/ui-${TAG}-ui.log"
"$UI_BIN" --open-page service --config "$UI_CONFIG" --worker "${OUT_DIR}/no-worker-for-shot" > "$UI_LOG" 2>&1 &
UI_PID=$!
sleep "$STARTUP_WAIT"
if ! kill -0 "$UI_PID" 2>/dev/null; then
  echo "正式实例在取景前就退出了（未驻守），本次截图无效: ${UI_LOG}" >&2
  cat "$UI_LOG" >&2 || true
  exit 1
fi

# 首拍几何必须在「第二次唤起」之前取（CodeRabbit 审查意见）：唤醒会在无窗时重建窗口，
# 之后再查会把「首启动没建窗」的回归放过去——那正是 #174 的形态。
WINDOW_INFO="${OUT_DIR}/ui-${TAG}-window.txt"
echo "---- window info（首拍，唤醒之前） ----"
osascript -e 'tell application "System Events" to tell process "PhotoPrivacy" to get {position, size} of window 1' > "$WINDOW_INFO" 2>&1 || true
cat "$WINDOW_INFO"

# 唤起 + 置前台：只服务截图（别截到壁纸/别的应用），不再承担判定职责。
"$UI_BIN" --open-page service --config "$UI_CONFIG" --worker "${OUT_DIR}/no-worker-for-shot" >/dev/null 2>&1 || true
sleep 3
osascript -e 'tell application "System Events" to tell process "PhotoPrivacy" to set frontmost to true' 2>&1 || true
sleep 1
WINDOW_AFTER_WAKE="${OUT_DIR}/ui-${TAG}-window-after-wake.txt"
osascript -e 'tell application "System Events" to tell process "PhotoPrivacy" to get {position, size} of window 1' > "$WINDOW_AFTER_WAKE" 2>&1 || true
cat "$WINDOW_AFTER_WAKE"
pgrep -fl "PhotoPrivacy" 2>&1 || true

# 配对状态对照（#172 根因对照，GUI 用户语境）：legacy `launchctl list <label>` 按调用者域
# 查找（查不到系统 LaunchDaemon），`launchctl print system/<label>` 显式查系统域（MacProbe
# 事实源）——两路并排留证据，截图状态与哪一路一致一目了然。
{
  echo "---- launchctl list com.photoprivacy.cleaner（legacy，调用者域） ----"
  launchctl list com.photoprivacy.cleaner 2>&1 || true
  echo "---- launchctl print system/com.photoprivacy.cleaner（显式系统域，MacProbe 事实源） ----"
  launchctl print system/com.photoprivacy.cleaner 2>&1 || true
} > "$LAUNCHCTL_TXT"
echo "---- launchctl 配对状态对照 ----"
cat "$LAUNCHCTL_TXT" || true

echo "---- screencapture ----"
screencapture -x "$PNG" || echo "screencapture failed (TCC 屏幕录制权限或无图形会话?)"

# 收尾清理：先 TERM，不退就 KILL，不让 UI 残留
kill "$UI_PID" 2>/dev/null || true
WAITED=0
while kill -0 "$UI_PID" 2>/dev/null; do
  if [[ "$WAITED" -ge 5 ]]; then
    kill -9 "$UI_PID" 2>/dev/null || true
    break
  fi
  sleep 1
  WAITED=$((WAITED + 1))
done
wait "$UI_PID" 2>/dev/null || true

# #174 硬性要求：静默配置 + --open-page service 必须在**首拍**就建窗。osascript 取不到几何
# 说明窗口没建（无窗报 Invalid index / 权限问题），此时整屏截图照样是「非空桌面帧」——只看 PNG
# 会假绿。取的是唤醒之前那一拍，避免第二次实例把窗口补出来掩盖回归。
if ! ui_window_geometry "$WINDOW_INFO"; then
  # 只有 System Events 的 Invalid index 才等于「进程在、窗口没建」；别的报错是截图通道
  # 本身没跑通（权限/会话），不能算 #174 的结论（CodeRabbit 审查意见）。
  if ! grep -q "Invalid index" "$WINDOW_INFO"; then
    echo "窗口几何取不到且不是 Invalid index（osascript/权限/会话问题），本次截图无效: ${WINDOW_INFO}" >&2
    cat "$WINDOW_INFO" >&2
    exit 1
  fi
  echo "静默配置下首启动未建窗（#174）：唤醒前取不到窗口几何，见 ${WINDOW_INFO}" >&2
  if ui_window_geometry "$WINDOW_AFTER_WAKE"; then
    echo "（唤醒之后才有窗：正是这条要求要抓的『静默吞掉请求页、靠第二次唤起才补上』形态）" >&2
  fi
  exit 1
fi
if [[ "$SILENT_APPLICABLE" -eq 0 ]]; then
  echo "注意：服务已安装，本 tag 的建窗不构成 #174 证据（静默未生效），只作截图前置检查"
fi

if [[ -s "$PNG" ]]; then
  echo "screenshot ok: ${PNG} ($(wc -c < "$PNG") bytes)"
  exit 0
fi
echo "screenshot missing/empty: ${PNG}" >&2
exit 1
