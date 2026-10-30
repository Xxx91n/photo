#!/usr/bin/env bash
set -euo pipefail

VERSION="0.1.0-preview"
FRAMEWORK="net10.0"
SELF_CONTAINED="true"
ZIP="true"

usage() {
  cat <<EOF
Usage: $0 [options]

Options:
  --version <ver>       Version string (default: $VERSION; 仅写入包名，Rust UI 产物不带版本元数据)
  --runtime <rid>       Runtime identifier (default: 构建主机对应 RID)
  --framework <fw>      Target framework (default: $FRAMEWORK; 仅作用于 Worker)
  --self-contained <b>  Self-contained (default: $SELF_CONTAINED)
  --zip <b>             Create archive (default: $ZIP)
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

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --runtime) RUNTIME="$2"; shift 2 ;;
    --framework) FRAMEWORK="$2"; shift 2 ;;
    --self-contained) SELF_CONTAINED="$2"; shift 2 ;;
    --zip) ZIP="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

# 判定放在参数解析之后：-h 与显式 --runtime 必须在任何主机上可用。
if [[ -z "$RUNTIME" ]]; then
  echo "无法从构建主机推导发布 RID（$(uname -s)-$(uname -m)）：Windows 请用 scripts/publish-app.ps1，或显式传 --runtime <rid>" >&2
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
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

echo "Publishing new UI (cargo) + PhotoPrivacy.Worker ..."
echo "Runtime: ${RUNTIME} | Framework: ${FRAMEWORK}"

WORKER_PUB_DIR="${TARGET_DIR}/worker"
rm -rf "$TARGET_DIR"
mkdir -p "$WORKER_PUB_DIR"

# Design(#49): 默认入口为新 UI——cargo 产物 photoprivacy-ui 复制为 PhotoPrivacy 作为发布入口；
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

rm -rf "$WORKER_PUB_DIR"

if [[ "$DO_ZIP" == true ]]; then
  TARBALL="${RELEASE_ROOT}/PhotoPrivacy-${VERSION}-${RUNTIME}.tar.gz"
  rm -f "$TARBALL"
  tar -czf "$TARBALL" -C "$TARGET_DIR" .
  echo "Package created: $TARBALL"
fi

echo "Publish done: $TARGET_DIR"
