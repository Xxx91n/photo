#!/usr/bin/env bash
# #33: 官方版下载链验证——把发布脚本里钉死的 SHA256 与 exiftool.org/checksums.txt 对账，
# 并对下载 URL 做可达性软检查。钉死值漂移 / 归档被替换会在这里 fail；上游换版导致的「查不到
# 该版本行」按 #222 降级为归档实算（见下），不再无条件判死。
# 依据：https://exiftool.org/checksums.txt；两个脚本取的是不同官方归档（Unix tar.gz / Windows _64.zip）。
#
# #222: checksums.txt 是 mutable 文本——官方只保留当前最新版，钉死版本一旦不再是最新，它那
# 几行就会整行消失。旧口径把「查不到该版本行」判硬失败，于是上游每发一次版本，本工作流在
# 不改钉死值的 PR 上恒红，与仓库代码正确性无关（不是代码回归，是校验设计缺陷）。现分三层：
#   1) 官方行在  → 与钉死值逐字比对，不一致即 fail（唯一由上游签发的权威值，保留硬对账）；
#   2) 官方行缺  → 降级为 warn，改对归档 URL 实算 SHA256 与钉死值比对：归档仍在且内容未被
#                  替换就以实算自证，不依赖 mutable 文本；归档被替换/半截照样 fail；
#   3) 兜底未判定 → 下载不通或无 sha256 工具时记「未判定」并计入结论，绝不冒充「已验证通过」。
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_SH="${REPO_ROOT}/scripts/publish.sh"
PUBLISH_PS1="${REPO_ROOT}/scripts/publish-app.ps1"
# 默认官方源；可用环境变量覆盖——本地回归时指向 fixture，复现「官方已删除该版本行」（#222）。
CHECKSUMS_URL="${CHECKSUMS_URL:-https://exiftool.org/checksums.txt}"

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

# 每种归档独立判定：官方行在就硬对账；缺失就登记为待实算（#222，不在此处判死）。
# URL 取各自发布脚本实际首选通道：publish.sh 用 downloads.sourceforge.net 的 tar.gz，
# publish-app.ps1 首选 master.dl.sourceforge.net 的 _64.zip?viasf=1。
TAR_URL="https://downloads.sourceforge.net/project/exiftool/Image-ExifTool-${VERSION}.tar.gz"
ZIP_URL="https://master.dl.sourceforge.net/project/exiftool/exiftool-${VERSION}_64.zip?viasf=1"

recalc_tar=0
recalc_zip=0
if [[ -n "$official_tar" ]]; then
  [[ "$pinned_tar" == "$official_tar" ]] || fail "publish.sh 钉死值与官方不符：pinned=${pinned_tar} official=${official_tar}"
  pass "官方 tar.gz SHA2-256 = ${official_tar}（与 publish.sh 一致）"
else
  warn "官方 checksums.txt 无 Image-ExifTool-${VERSION}.tar.gz 行（mutable 文本只保留当前最新版）→ 降级为归档实算（#222）"
  recalc_tar=1
fi
if [[ -n "$official_zip" ]]; then
  [[ "$pinned_zip" == "$official_zip" ]] || fail "publish-app.ps1 钉死值与官方不符：pinned=${pinned_zip} official=${official_zip}"
  pass "官方 _64.zip SHA2-256 = ${official_zip}（与 publish-app.ps1 一致）"
else
  warn "官方 checksums.txt 无 exiftool-${VERSION}_64.zip 行（mutable 文本只保留当前最新版）→ 降级为归档实算（#222）"
  recalc_zip=1
fi

# 2. 官方行缺失时的兜底：实算归档 SHA256 与钉死值比对（#222）。能证明的只有一件事——
#    钉死值仍对得上真实归档；证明不了的（下载失败、无 sha256 工具）如实记「未判定」。
#    必须让 curl 跟随重定向：SourceForge 首跳 302 到镜像，不跟随只会拿到跳转页（#164 踩过）。
sha256_of_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}' | tr 'A-F' 'a-f'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}' | tr 'A-F' 'a-f'
  else
    return 1
  fi
}

download_archive() {
  local url="$1" dest="$2"
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL --retry 3 --max-time 300 -o "$dest" "$url"
  elif command -v wget >/dev/null 2>&1; then
    wget -q -O "$dest" "$url"
  else
    return 1
  fi
}

recalc_total=0
undetermined=0
recalc_by_archive() {
  local label="$1" url="$2" pinned="$3" tmp actual attempt last_actual=""
  recalc_total=$((recalc_total + 1))
  tmp="$(mktemp "${TMPDIR:-/tmp}/exiftool-verify.XXXXXX")"
  # 最多取两次：SourceForge 偶发返回拦截页/半截（#164 踩过），单次不符就 fail 会造出
  # 「与仓库正确性无关的红」，正是本票要消除的东西；重取后仍不符才判定归档有问题。
  for attempt in 1 2; do
    if download_archive "$url" "$tmp"; then
      actual="$(sha256_of_file "$tmp" || true)"
      if [[ -z "$actual" ]]; then
        rm -f "$tmp"
        undetermined=$((undetermined + 1))
        warn "无 sha256sum/shasum，${label} 实算未判定"
        return 0
      fi
      if [[ "$actual" == "$pinned" ]]; then
        rm -f "$tmp"
        pass "${label} 归档实算 SHA256 = ${actual}（与钉死值一致）"
        return 0
      fi
      last_actual="$actual"
      warn "${label} 第 ${attempt} 次下载内容与钉死值不符（actual=${actual}），重取以排除 CDN 拦截页"
    else
      warn "${label} 第 ${attempt} 次下载失败"
    fi
  done
  rm -f "$tmp"
  if [[ -n "$last_actual" ]]; then
    fail "${label} 归档实算 SHA256 与钉死值不符（重取后仍不符）：pinned=${pinned} actual=${last_actual}（归档可能被替换或半截）"
  fi
  undetermined=$((undetermined + 1))
  warn "${label} 归档两次下载均失败，实算未判定（软兜底，不参与硬判定）：${url}"
}

echo "=== 2. 归档实算 SHA256（官方行缺失时兜底，#222）==="
if [[ "$recalc_tar" -eq 0 && "$recalc_zip" -eq 0 ]]; then
  pass "官方 checksums.txt 两条行齐全，无需实算兜底"
else
  [[ "$recalc_tar" -eq 0 ]] || recalc_by_archive "publish.sh tar.gz" "$TAR_URL" "$pinned_tar"
  [[ "$recalc_zip" -eq 0 ]] || recalc_by_archive "publish-app.ps1 _64.zip" "$ZIP_URL" "$pinned_zip"
fi

# 3. 下载 URL 可达性——SourceForge 有跳转/拦截页历史（#164），HEAD 失败不等于 GET 失败，
#    故仅作软检查并如实计入结论，绝不把「未判定」说成「通过」。第 2 步已实际下载过的归档
#    本身就是可达证据，不再重复探测。
echo "=== 3. 下载 URL 可达性（软检查，不参与硬判定）==="
url_total=0
url_unconfirmed=0
probe_url() {
  local url="$1"
  url_total=$((url_total + 1))
  if command -v curl >/dev/null 2>&1; then
    # -f：HTTP 4xx/5xx 视为失败（不加时 curl 对 404 仍返回 0，会把失效 URL 报成"可达"）。
    if curl -fsSIL --max-time 20 -o /dev/null "$url"; then
      pass "可达：${url}"
    else
      url_unconfirmed=$((url_unconfirmed + 1))
      warn "URL 探测未成功：${url}"
    fi
  else
    url_unconfirmed=$((url_unconfirmed + 1))
    warn "无 curl，URL 可达性未判定：${url}"
  fi
}
[[ "$recalc_tar" -eq 1 ]] || probe_url "$TAR_URL"
[[ "$recalc_zip" -eq 1 ]] || probe_url "$ZIP_URL"
if [[ "$url_total" -eq 0 ]]; then
  # 两条都走了兜底实算，第 3 步没有可探测项。只有实算确实下载成功（undetermined=0）
  # 才算「已由第 2 步确认可达」；否则如实报未判定，不能把下载失败说成可达（#223 审查意见）。
  if [[ "$undetermined" -eq 0 ]]; then
    pass "两条归档均已由第 2 步实算下载确认可达，跳过 HEAD 探测"
  else
    warn "第 2 步实算未能下载全部归档，未做 HEAD 探测（未判定项见结论，不重复探测）"
  fi
fi

# 结论只宣称已真正验证的部分：#222 之后「官方行缺失」不再等于失败，但也绝不等于通过——
# 必须实算命中钉死值才算自证；未判定的项逐条报出，由人决定是否追查。
if [[ "$undetermined" -eq 0 && "$url_unconfirmed" -eq 0 ]]; then
  echo "PASS: ExifTool ${VERSION} 钉死值已确认——官方 checksums.txt 对账与归档实算均一致"
else
  echo "PASS(部分未判定): ExifTool ${VERSION} 钉死值未发现漂移，但下列校验源未判定，不构成「已验证通过」"
  if [[ "$undetermined" -gt 0 ]]; then
    echo "      - 归档实算未判定 ${undetermined}/${recalc_total} 项（官方行缺失且下载或 sha256 工具不可用）"
  fi
  if [[ "$url_unconfirmed" -gt 0 ]]; then
    echo "      - 下载 URL 可达性未确认 ${url_unconfirmed}/${url_total} 项（软检查，不参与本脚本判定）"
  fi
  echo "      发布链以「取回归档后 SHA256 校验不过就重取」为硬判定（见 #164）。"
fi
