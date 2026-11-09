#!/usr/bin/env bash
# #33: ExifToolPathOverride 构建层自检——纯 shell，不编译应用、不联网。
# 覆盖 publish.sh 的目录式打包路径：布局校验、产物落盘、-ver 探针、错误路径 fail-closed。
# CI 与本地 WSL 同一套规则；publish.sh 被 source 时不进入 cargo/dotnet 主流程。
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_SH="${REPO_ROOT}/scripts/publish.sh"

fail() { echo "FAIL: $*" >&2; exit 1; }
pass() { echo "  ok: $*"; }

[[ -f "$PUBLISH_SH" ]] || fail "publish.sh not found: $PUBLISH_SH"
bash -n "$PUBLISH_SH" || fail "publish.sh syntax error"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

OVERRIDE="${WORK}/override"
DEST="${WORK}/release/linux-x64"
mkdir -p "${OVERRIDE}/lib"
mkdir -p "$DEST"
printf 'lib-marker\n' > "${OVERRIDE}/lib/marker.txt"
printf 'license-stub\n' > "${OVERRIDE}/README"
cat > "${OVERRIDE}/exiftool" <<'STUB'
#!/usr/bin/env bash
if [[ "${1:-}" == "-ver" ]]; then echo "13.60-override-stub"; fi
STUB
chmod +x "${OVERRIDE}/exiftool"

# source 只暴露函数；TARGET_DIR 由测试自行注入。屏蔽网络工具并记下任何调用，
# 用「零调用」直接证明覆盖路径跳过下载，而不是仅靠下载失败反证。
export TARGET_DIR="$DEST"
set -- # 清空位置参数，避免 source 时误解析
source "$PUBLISH_SH"
NET_CALLS="${WORK}/network-calls.log"
export NET_CALLS
curl() { printf 'curl %s\n' "$*" >> "$NET_CALLS"; return 1; }
wget() { printf 'wget %s\n' "$*" >> "$NET_CALLS"; return 1; }
export -f curl wget 2>/dev/null || true

echo "=== 1. bundle_exiftool_from_override：目录式覆盖打包 ==="
bundle_exiftool_from_override "$OVERRIDE" >/dev/null
[[ -f "${DEST}/ExifTool/exiftool" ]] || fail "dest 缺少 exiftool"
[[ -x "${DEST}/ExifTool/exiftool" ]] || fail "dest exiftool 不可执行"
[[ -f "${DEST}/ExifTool/lib/marker.txt" ]] || fail "dest 缺少 lib/"
[[ -f "${DEST}/ExifTool/README" ]] || fail "dest 缺少 README"
pass "布局与产物落盘"

echo "=== 2. bundle_exiftool 带 override：跳过下载 ==="
rm -rf "${DEST}/ExifTool"
# 若误走默认下载通道，curl/wget 已被屏蔽且会 exit 1，此调用即失败。
bundle_exiftool "13.60" "" "$OVERRIDE" >/dev/null
[[ -f "${DEST}/ExifTool/exiftool" ]] || fail "override 未生效（可能误走下载通道）"
[[ ! -s "$NET_CALLS" ]] || fail "覆盖路径发起了网络下载：$(cat "$NET_CALLS")"
pass "override 生效且下载函数零调用（不联网）"

echo "=== 3. 错误路径 fail-closed ==="
if ( bundle_exiftool_from_override "${WORK}/does-not-exist" >/dev/null 2>&1 ); then
  fail "非目录覆盖应被拒绝"
fi
pass "非目录覆盖被拒绝"

NO_LIB="${WORK}/no-lib"
mkdir -p "$NO_LIB"
cp "${OVERRIDE}/exiftool" "${NO_LIB}/exiftool"
if ( bundle_exiftool_from_override "$NO_LIB" >/dev/null 2>&1 ); then
  fail "缺 lib/ 的覆盖应被拒绝"
fi
pass "缺 lib/ 覆盖被拒绝"

echo "PASS: ExifToolPathOverride 构建层自检全部通过"
