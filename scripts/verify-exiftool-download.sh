#!/usr/bin/env bash
# #33: 官方版下载链验证——把发布脚本里钉死的 SHA256 与 exiftool.org/checksums.txt 对账，
# 并对下载 URL 做可达性软检查。钉死值漂移 / 官方换版 / 归档被替换会在这里 fail。
# 依据：https://exiftool.org/checksums.txt；两个脚本取的是不同官方归档（Unix tar.gz / Windows _64.zip）。
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_SH="${REPO_ROOT}/scripts/publish.sh"
PUBLISH_PS1="${REPO_ROOT}/scripts/publish-app.ps1"
CHECKSUMS_URL="https://exiftool.org/checksums.txt"

fail() { echo "FAIL: $*" >&2; exit 1; }
pass() { echo "  ok: $*"; }
warn() { echo "WARN: $*" >&2; }

VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
  VERSION="$(sed -nE 's/^EXIFTOOL_VERSION="([^"]+)".*/\1/p' "$PUBLISH_SH" | head -1)"
fi
[[ -n "$VERSION" ]] || fail "无法从 publish.sh 解析 EXIFTOOL_VERSION"
echo "ExifTool version under verification: ${VERSION}"

# 钉死值来源（单一事实源就是两个发布脚本本身，避免再引入第三份清单）。
# 提取统一包一层 `{ ...; } || true`：set -euo pipefail 下 grep 无匹配会让赋值处直接退出，
# 那样「官方换版 / 钉死值缺失」这一核心场景只会留下无信息的非零退出，下面的 fail 提示到不了。
pinned_tar="$({ sed -nE "s/^[[:space:]]*${VERSION//./\\.}\)[[:space:]]+echo[[:space:]]+\"([0-9a-fA-F]{64})\".*/\1/p" "$PUBLISH_SH" | head -1 | tr 'A-F' 'a-f'; } || true)"
pinned_zip="$({ grep -iE "\"${VERSION//./\\.}\"[[:space:]]*=[[:space:]]*\"[0-9a-fA-F]{64}\"" "$PUBLISH_PS1" \
  | sed -E 's/.*"([0-9a-fA-F]{64})".*/\1/' | head -1 | tr 'A-F' 'a-f'; } || true)"

[[ -n "$pinned_tar" ]] || fail "publish.sh 未登记 ${VERSION} 的 tar.gz SHA256"
[[ -n "$pinned_zip" ]] || fail "publish-app.ps1 未登记 ${VERSION} 的 _64.zip SHA256"
pass "publish.sh  钉死 tar.gz = ${pinned_tar}"
pass "publish-app.ps1 钉死 _64.zip = ${pinned_zip}"

# 取官方 checksums.txt：curl → python3(urllib) → wget，覆盖 CI runner 与最小 WSL。
fetch_checksums() {
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL --retry 3 --max-time 60 "$CHECKSUMS_URL"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import sys,urllib.request;sys.stdout.write(urllib.request.urlopen(sys.argv[1],timeout=60).read().decode("utf-8","replace"))' "$CHECKSUMS_URL"
  elif command -v wget >/dev/null 2>&1; then
    wget -q -O - "$CHECKSUMS_URL"
  else
    echo "no HTTP client (curl/python3/wget)" >&2
    return 1
  fi
}

echo "=== 1. 官方 checksums.txt 对账 ==="
checksums="$(fetch_checksums)" || fail "无法获取官方 checksums.txt（下载链不可达即视为验证失败）"
[[ -n "$checksums" ]] || fail "官方 checksums.txt 为空"

official_tar="$({ printf '%s\n' "$checksums" \
  | grep -iE "SHA2-256\(Image-ExifTool-${VERSION//./\\.}\.tar\.gz\)" \
  | sed -E 's/.*=[[:space:]]*([0-9a-fA-F]{64}).*/\1/' | head -1 | tr 'A-F' 'a-f'; } || true)"
official_zip="$({ printf '%s\n' "$checksums" \
  | grep -iE "SHA2-256\(exiftool-${VERSION//./\\.}_64\.zip\)" \
  | sed -E 's/.*=[[:space:]]*([0-9a-fA-F]{64}).*/\1/' | head -1 | tr 'A-F' 'a-f'; } || true)"

[[ -n "$official_tar" ]] || fail "官方 checksums.txt 无 Image-ExifTool-${VERSION}.tar.gz 的 SHA2-256 行"
[[ -n "$official_zip" ]] || fail "官方 checksums.txt 无 exiftool-${VERSION}_64.zip 的 SHA2-256 行"
[[ "$pinned_tar" == "$official_tar" ]] || fail "publish.sh 钉死值与官方不符：pinned=${pinned_tar} official=${official_tar}"
[[ "$pinned_zip" == "$official_zip" ]] || fail "publish-app.ps1 钉死值与官方不符：pinned=${pinned_zip} official=${official_zip}"
pass "官方 tar.gz SHA2-256 = ${official_tar}（与 publish.sh 一致）"
pass "官方 _64.zip SHA2-256 = ${official_zip}（与 publish-app.ps1 一致）"

# 2. 下载 URL 可达性——SourceForge 有跳转/拦截页历史（#164），HEAD 失败不等于 GET 失败，
#    故仅作软检查并如实计入结论，绝不把「未判定」说成「通过」。硬判定是本脚本第 1 步的
#    官方 SHA256 对账，以及发布脚本「取回归档后校验不过就重取」。
#    URL 取各自发布脚本实际首选通道：publish.sh 用 downloads.sourceforge.net 的 tar.gz，
#    publish-app.ps1 首选 master.dl.sourceforge.net 的 _64.zip?viasf=1。
echo "=== 2. 下载 URL 可达性（软检查，不参与硬判定）==="
TAR_URL="https://downloads.sourceforge.net/project/exiftool/Image-ExifTool-${VERSION}.tar.gz"
ZIP_URL="https://master.dl.sourceforge.net/project/exiftool/exiftool-${VERSION}_64.zip?viasf=1"
url_total=0
url_unconfirmed=0
for url in "$TAR_URL" "$ZIP_URL"; do
  url_total=$((url_total + 1))
  if command -v curl >/dev/null 2>&1; then
    if curl -sSIL --max-time 20 -o /dev/null "$url"; then
      pass "可达：${url}"
    else
      url_unconfirmed=$((url_unconfirmed + 1))
      warn "URL 探测未成功：${url}"
    fi
  else
    url_unconfirmed=$((url_unconfirmed + 1))
    warn "无 curl，URL 可达性未判定：${url}"
  fi
done

# 结论只宣称已真正验证的部分：钉死值与官方 checksums.txt 一致。
echo "PASS: 官方 SHA256 钉死值与 checksums.txt 对账一致（${VERSION}）"
if [[ "$url_unconfirmed" -gt 0 ]]; then
  echo "NOTE: 下载 URL 可达性 ${url_unconfirmed}/${url_total} 未确认（软检查，不参与本脚本判定）；"
  echo "      发布链以「取回归档后 SHA256 校验不过就重取」为硬判定（见 #164）。"
fi
