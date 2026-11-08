#!/usr/bin/env bash
set -euo pipefail

VERSION="0.1.0-preview"
FRAMEWORK="net10.0"
SELF_CONTAINED="true"
ZIP="true"
EXIFTOOL_VERSION="13.61"
EXIFTOOL_ARCHIVE=""
# #33: ExifToolPathOverride——指向已安装的 ExifTool 目录，直接打包、跳过下载，
# 对齐 ExifToolGUI 的 ETOverrideDir（自带 ExifTool、不下载）。空 = 走默认下载通道。
EXIFTOOL_PATH_OVERRIDE=""

# 固定版本 → 官方 SHA256（取自 https://exiftool.org/checksums.txt）。
# 新增版本必须同时登记校验值：未登记的版本直接失败，避免打包被替换或半截的归档。
# 用 case 而不是关联数组：macOS runner 默认 bash 3.2 不支持 declare -A。
exiftool_sha256() {
  case "$1" in
    13.61) echo "df7f42c14b4f58a60bec39feae0ee9593000ee27e44920d2b0a21c6627de0baa" ;;
    13.60) echo "c8e3c42576f46d862e4cd9c192989776e7363931e890c95056bcd6293e83ea36" ;;
    *) return 1 ;;
  esac
}

usage() {
  cat <<EOF
Usage: $0 [options]

Options:
  --version <ver>       Version string (default: $VERSION; 仅写入包名，Rust UI 产物不带版本元数据)
  --runtime <rid>       Runtime identifier (default: 构建主机对应 RID)
  --framework <fw>      Target framework (default: $FRAMEWORK; 仅作用于 Worker)
  --self-contained <b>  Self-contained (default: $SELF_CONTAINED)
  --zip <b>             Create archive (default: $ZIP)
  --exiftool-version <v>  随包内置的 ExifTool 版本（default: $EXIFTOOL_VERSION；必须已登记校验值）
  --exiftool-archive <p>  使用本地已下载的 Image-ExifTool-<v>.tar.gz（离线/内网构建；仍校验 SHA256）
  --exiftool-path-override <d>  使用已安装的 ExifTool 目录 <d>（含 exiftool + lib/），直接打包跳过下载（#33）
  -h, --help            Show this help

Valid runtimes: linux-x64, linux-arm64, osx-x64, osx-arm64 (须在本机对应平台执行；
GUI 入口为 gpui 新 UI，不支持交叉编译。win-x64 走 Windows 机的 publish-app.ps1)
EOF
}

# 默认 RID 由构建主机推导（gpui 新 UI 不支持交叉编译，见下方 host-match 检查）。
RUNTIME=""
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) RUNTIME="linux-x64" ;;
  Linux-aarch64) RUNTIME="linux-arm64" ;;
  Darwin-x86_64) RUNTIME="osx-x64" ;;
  Darwin-arm64) RUNTIME="osx-arm64" ;;
esac

# 仅当作为可执行脚本直接运行时才进入参数解析/环境校验/发布主流程；
# 被 source 时只暴露顶部函数与默认变量，供 #33 覆盖逻辑单测（WSL 无 cargo 也能安全载入）。
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --runtime) RUNTIME="$2"; shift 2 ;;
    --framework) FRAMEWORK="$2"; shift 2 ;;
    --self-contained) SELF_CONTAINED="$2"; shift 2 ;;
    --zip) ZIP="$2"; shift 2 ;;
    --exiftool-version) EXIFTOOL_VERSION="$2"; shift 2 ;;
    --exiftool-archive) EXIFTOOL_ARCHIVE="$2"; shift 2 ;;
    --exiftool-path-override) EXIFTOOL_PATH_OVERRIDE="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

# 判定放在参数解析之后：-h 与显式 --runtime 必须在任何主机上可用。
if [[ -z "$RUNTIME" ]]; then
  echo "无法从构建主机推导发布 RID（$(uname -s)-$(uname -m)）：Windows 请用 scripts/publish-app.ps1，或显式传 --runtime <rid>" >&2
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RELEASE_ROOT="${REPO_ROOT}/release"
TARGET_DIR="${RELEASE_ROOT}/${RUNTIME}"

VALID_RIDS="linux-x64 linux-arm64 osx-x64 osx-arm64"
rid_valid=false
for rid in $VALID_RIDS; do
  if [[ "$RUNTIME" == "$rid" ]]; then rid_valid=true; break; fi
done
if [[ "$rid_valid" == false ]]; then
  echo "Invalid runtime '$RUNTIME'. Valid: $VALID_RIDS" >&2
  echo "win-x64 releases are produced on Windows via scripts/publish-app.ps1 (gpui 新 UI 不支持交叉编译)" >&2
  exit 1
fi

case "$SELF_CONTAINED" in
  1|true|yes|on) SC=true ;;
  *) SC=false ;;
esac

case "$ZIP" in
  1|true|yes|on) DO_ZIP=true ;;
  *) DO_ZIP=false ;;
esac

if [[ "$FRAMEWORK" != "net10.0" ]]; then
  echo "Framework must be net10.0, got: $FRAMEWORK" >&2
  exit 1
fi

# gpui 新 UI 不支持交叉编译：RID 必须与构建主机一致。
HOST_OS="$(uname -s)"
HOST_ARCH="$(uname -m)"
case "$RUNTIME" in
  linux-x64)
    [[ "$HOST_OS" == "Linux" && "$HOST_ARCH" == "x86_64" ]] || { echo "Runtime linux-x64 requires a Linux x86_64 build host (got $HOST_OS $HOST_ARCH)" >&2; exit 1; } ;;
  linux-arm64)
    [[ "$HOST_OS" == "Linux" && "$HOST_ARCH" == "aarch64" ]] || { echo "Runtime linux-arm64 requires a Linux arm64 build host (got $HOST_OS $HOST_ARCH)" >&2; exit 1; } ;;
  osx-x64)
    [[ "$HOST_OS" == "Darwin" && "$HOST_ARCH" == "x86_64" ]] || { echo "Runtime osx-x64 requires a macOS x86_64 build host (got $HOST_OS $HOST_ARCH)" >&2; exit 1; } ;;
  osx-arm64)
    [[ "$HOST_OS" == "Darwin" && "$HOST_ARCH" == "arm64" ]] || { echo "Runtime osx-arm64 requires a macOS arm64 build host (got $HOST_OS $HOST_ARCH)" >&2; exit 1; } ;;
esac

command -v cargo >/dev/null 2>&1 || { echo "cargo not found in PATH; 新 UI 入口需要 Rust 工具链（rust-version 见 ui/Cargo.toml）" >&2; exit 1; }

fi # guard 第一段结束：参数解析 + 主机/工具链校验只在直接执行时跑

# --- 内置 ExifTool（#108）---
# Unix 官方发行形态是 Perl 脚本 + lib/（没有自包含二进制），随包带上 exiftool 与 lib/；
# 运行时仍要求目标机有 perl（macOS 15 仍带 /usr/bin/perl5.34，Apple 已声明未来可能移除）。
# 配置模板的 exiftool.path 留空，运行时探测优先命中 <包根>/ExifTool/。
bundle_exiftool() {
  local version="$1"
  local archive="$2"
  # 第三参数可省略：source 后单测或旧的两参数调用不因 set -u 报错。
  local override="${3:-}"

  # #33: ExifToolPathOverride——目录式覆盖，直接打包跳过下载（对齐 ExifToolGUI ETOverrideDir）。
  if [[ -n "$override" ]]; then
    # 两个来源互斥：同时给会让 --exiftool-archive 被静默忽略（其语义是「仍校验 SHA256」）。
    if [[ -n "$archive" ]]; then
      echo "--exiftool-path-override 与 --exiftool-archive 互斥：覆盖模式下不会读取归档，请只传一个" >&2
      exit 1
    fi
    bundle_exiftool_from_override "$override"
    return
  fi

  local dest="${TARGET_DIR}/ExifTool"
  local expected
  expected="$(exiftool_sha256 "$version" || true)"

  if [[ -z "$expected" ]]; then
    echo "No pinned SHA256 for ExifTool $version; 先登记 scripts/publish.sh 的 exiftool_sha256 函数（https://exiftool.org/checksums.txt）" >&2
    exit 1
  fi

  local tmp
  tmp="$(mktemp -d)"

  if [[ -z "$archive" ]]; then
    archive="${tmp}/Image-ExifTool-${version}.tar.gz"
    local url="https://downloads.sourceforge.net/project/exiftool/Image-ExifTool-${version}.tar.gz"
    echo "Downloading ExifTool ${version} ..."
    if command -v curl >/dev/null 2>&1; then
      curl -fsSL --retry 3 -o "$archive" "$url" || { echo "ExifTool download failed: $url" >&2; exit 1; }
    else
      wget -q -O "$archive" "$url" || { echo "ExifTool download failed: $url" >&2; exit 1; }
    fi
  elif [[ ! -f "$archive" ]]; then
    echo "ExifTool archive not found: $archive" >&2
    exit 1
  fi

  local actual
  if command -v sha256sum >/dev/null 2>&1; then
    actual="$(sha256sum "$archive" | awk '{print $1}')"
  else
    actual="$(shasum -a 256 "$archive" | awk '{print $1}')"
  fi

  if [[ "$actual" != "$expected" ]]; then
    echo "ExifTool archive SHA256 mismatch for $version: expected $expected, got $actual" >&2
    exit 1
  fi

  tar -xzf "$archive" -C "$tmp"
  local src="${tmp}/Image-ExifTool-${version}"
  [[ -f "${src}/exiftool" && -d "${src}/lib" ]] || { echo "Unexpected ExifTool archive layout: $src" >&2; exit 1; }

  mkdir -p "$dest"
  cp "${src}/exiftool" "${dest}/exiftool"
  cp -R "${src}/lib" "${dest}/lib"
  # 许可证与版权声明随包（README 内含 Artistic/GPL 双许可全文）。
  cp "${src}/README" "${dest}/README"
  chmod +x "${dest}/exiftool"
  rm -rf "$tmp"

  if ! command -v perl >/dev/null 2>&1; then
    echo "WARNING: perl not found on build host; 内置 ExifTool 需要目标机提供 perl（Unix 官方发行形态）" >&2
  fi
  echo "Bundled ExifTool ${version} -> ${dest}"
}

# #33: ExifToolPathOverride——从已安装的 ExifTool 目录直接打包，跳过下载与 SHA256 校验
# （对齐 ExifToolGUI 的 ETOverrideDir：用户自带 ExifTool、不下载）。仅校验布局并做 -ver 版本探针。
bundle_exiftool_from_override() {
  local override="$1"
  local dest="${TARGET_DIR}/ExifTool"

  if [[ ! -d "$override" ]]; then
    echo "ExifToolPathOverride 不是目录：$override" >&2
    exit 1
  fi

  local exe="${override}/exiftool"
  local lib="${override}/lib"
  if [[ ! -f "$exe" || ! -d "$lib" ]]; then
    echo "ExifToolPathOverride 布局不符：需 ${exe} 与 ${lib}（Unix Perl 发行版必备同级 lib/）" >&2
    exit 1
  fi

  mkdir -p "$dest"
  cp "$exe" "${dest}/exiftool"
  cp -R "$lib" "${dest}/lib"
  if [[ -f "${override}/README" ]]; then
    cp "${override}/README" "${dest}/README"
  fi
  chmod +x "${dest}/exiftool"

  # 版本探针：对齐 ExifToolGUI 的 -ver 校验，确认覆盖的是可用 ExifTool。缺 perl 时退化为警告（不致命）。
  if command -v perl >/dev/null 2>&1; then
    local ver
    ver="$(${dest}/exiftool -ver 2>/dev/null || true)"
    if [[ -z "$ver" ]]; then
      echo "WARNING: ExifToolPathOverride 的 exiftool -ver 未返回版本（可能缺 perl 或安装损坏）" >&2
    else
      echo "Bundled ExifTool from override (${override}) version ${ver} -> ${dest}"
    fi
  else
    echo "WARNING: perl 不在构建主机，无法运行 exiftool -ver 探针；覆盖目录仍已打包" >&2
  fi
}

# guard 第二段：主流程只在直接执行时跑；source 时上面两个 bundle 函数仍可被单测调用（#33）。
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then

echo "Publishing new UI (cargo) + PhotoPrivacy.Worker ..."
echo "Runtime: ${RUNTIME} | Framework: ${FRAMEWORK}"

WORKER_PUB_DIR="${TARGET_DIR}/worker"
rm -rf "$TARGET_DIR"
mkdir -p "$WORKER_PUB_DIR"

# Design(#245): 默认入口为新 UI——cargo 产物 photoprivacy-ui 复制为 PhotoPrivacy 作为发布入口；
# MSVC 清单问题仅存在于 Windows（见 ui/crates/photoprivacy-ui/build.rs），Unix 主机用宿主默认 target。
cargo build --release --locked --manifest-path "${REPO_ROOT}/ui/Cargo.toml" -p photoprivacy-ui

dotnet publish "${REPO_ROOT}/src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj" \
  -c Release \
  -f "$FRAMEWORK" \
  -r "$RUNTIME" \
  --self-contained "$SC" \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:PublishTrimmed=false \
  /p:Version="$VERSION" \
  /p:UseAppHost=true \
  -o "$WORKER_PUB_DIR"

APP_EXE="PhotoPrivacy"
WORKER_EXE="PhotoPrivacyWorker"
UI_BIN="${REPO_ROOT}/ui/target/release/photoprivacy-ui"

[[ -f "$UI_BIN" ]] || { echo "UI executable not found: $UI_BIN" >&2; exit 1; }
[[ -f "${WORKER_PUB_DIR}/${WORKER_EXE}" ]] || { echo "Worker executable not found: ${WORKER_PUB_DIR}/${WORKER_EXE}" >&2; exit 1; }

# Copy install scripts to release/<rid>/scripts/
SCRIPTS_DIR="${TARGET_DIR}/scripts"
mkdir -p "$SCRIPTS_DIR"
case "$RUNTIME" in
  linux-*) cp "${REPO_ROOT}/scripts/install-systemd-service.sh" "$SCRIPTS_DIR/" ;;
  osx-*) cp "${REPO_ROOT}/scripts/install-launchd-service.sh" "$SCRIPTS_DIR/" ;;
esac

cp "$UI_BIN" "${TARGET_DIR}/${APP_EXE}"
cp "${WORKER_PUB_DIR}/${WORKER_EXE}" "${TARGET_DIR}/${WORKER_EXE}"
chmod +x "${TARGET_DIR}/${APP_EXE}" "${TARGET_DIR}/${WORKER_EXE}"

mkdir -p "${TARGET_DIR}/config"
cp "${REPO_ROOT}/config/config.sample.json" "${TARGET_DIR}/config/config.sample.json"
# Copy config.sample.json as config.json so the release has a working default config
cp "${REPO_ROOT}/config/config.sample.json" "${TARGET_DIR}/config/config.json"
cp "${REPO_ROOT}/config/README.md" "${TARGET_DIR}/config/README.md"
cp "${REPO_ROOT}/README.md" "${TARGET_DIR}/README.md"

bundle_exiftool "$EXIFTOOL_VERSION" "$EXIFTOOL_ARCHIVE" "$EXIFTOOL_PATH_OVERRIDE"

rm -rf "$WORKER_PUB_DIR"

if [[ "$DO_ZIP" == true ]]; then
  TARBALL="${RELEASE_ROOT}/PhotoPrivacy-${VERSION}-${RUNTIME}.tar.gz"
  rm -f "$TARBALL"
  tar -czf "$TARBALL" -C "$TARGET_DIR" .
  echo "Package created: $TARBALL"
fi

echo "Publish done: $TARGET_DIR"

fi # /if [[ "${BASH_SOURCE[0]}" == "$0" ]] —— source 时仅暴露函数
