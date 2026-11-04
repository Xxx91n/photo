#!/usr/bin/env bash
# capture-ui-service-page.sh —— #32 UI Service 页截图取证（以 GUI 用户运行）。
#
# 以 --open-page service 启动发布包里的 PhotoPrivacy，等首屏渲染后整屏截图，
# 连同时刻的 launchctl 状态一并留证，然后退出 UI。供 verify-launchd-service.sh
# 在各生命周期状态下调用（虚拟环境 = CI macOS runner；也可在实机手动执行）。
#
# #174：`--open-page` 显式覆盖静默启动（ui/crates/photoprivacy-ui/src/main.rs 的
# should_open_main_window），ui.hide_main_window_on_startup=true 的机器上首实例也直接建窗。
# 注意下面的第二次实例唤起只是兜「已有实例先跑起来」的单实例守卫，它对 #174 的回归不敏感
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

# --worker 指向不存在路径：截图场景只看 Service 页探测，不拉起后台 Worker
# （避免与服务 Worker 同 config 并存的单写者让位场景混入，#162）。
UI_LOG="${OUT_DIR}/ui-${TAG}-ui.log"
"$UI_BIN" --open-page service --worker "${OUT_DIR}/no-worker-for-shot" > "$UI_LOG" 2>&1 &
UI_PID=$!

# 首屏渲染余量：服务状态在建窗前已同步探测（main.rs 首拍），截图等窗口稳定。
sleep 4
# 单实例唤起兜底：若首实例静默驻守无窗，第二个实例只唤起不重开（main.rs 单实例守卫语义），
# 会把主窗带出来；再点名置前台，避免截图截到别的应用/壁纸。
"$UI_BIN" --open-page service --worker "${OUT_DIR}/no-worker-for-shot" >/dev/null 2>&1 || true
sleep 3
osascript -e 'tell application "System Events" to tell process "PhotoPrivacy" to set frontmost to true' 2>&1 || true
sleep 1

# 窗口实存与几何留证（无窗时本文件只有报错，配合 UI 日志定位）
echo "---- window info ----"
osascript -e 'tell application "System Events" to tell process "PhotoPrivacy" to get {position, size} of window 1' 2>&1 || true
pgrep -fl "PhotoPrivacy" 2>&1 || true

# 配对状态对照（#172 根因对照，GUI 用户语境）：legacy `launchctl list <label>` 按调用者域
# 查找（查不到系统 LaunchDaemon），`launchctl print system/<label>` 显式查系统域（MacProbe
# 事实源）——两路并排留证，截图状态与哪一路一致一目了然。
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

# 收口：先 TERM 后兜底 KILL，不让 UI 残留
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

if [[ -s "$PNG" ]]; then
  echo "screenshot ok: ${PNG} ($(wc -c < "$PNG") bytes)"
  exit 0
fi
echo "screenshot missing/empty: ${PNG}" >&2
exit 1
