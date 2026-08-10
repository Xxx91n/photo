#!/usr/bin/env bash
set -euo pipefail

VERSION="0.1.0-preview"
RUNTIME="win-x64"
FRAMEWORK="net10.0"
SELF_CONTAINED="true"
ZIP="true"

usage() {
  cat <<EOF
Usage: $0 [options]

Options:
  --version <ver>       Version string (default: $VERSION)
  --runtime <rid>       Runtime identifier (default: $RUNTIME)
  --framework <fw>      Target framework (default: $FRAMEWORK)
  --self-contained <b>  Self-contained (default: $SELF_CONTAINED)
  --zip <b>             Create archive (default: $ZIP)
  -h, --help            Show this help

Valid runtimes: win-x64, win-x86, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64
EOF
}

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

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RELEASE_ROOT="${REPO_ROOT}/release"
TARGET_DIR="${RELEASE_ROOT}/${RUNTIME}"

VALID_RIDS="win-x64 win-x86 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64"
rid_valid=false
for rid in $VALID_RIDS; do
  if [[ "$RUNTIME" == "$rid" ]]; then rid_valid=true; break; fi
done
if [[ "$rid_valid" == false ]]; then
  echo "Invalid runtime '$RUNTIME'. Valid: $VALID_RIDS" >&2
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

IS_UNIX=false
case "$RUNTIME" in
  linux-*|osx-*) IS_UNIX=true ;;
esac

if [[ "$FRAMEWORK" != "net10.0" ]]; then
  echo "Framework must be net10.0, got: $FRAMEWORK" >&2
  exit 1
fi

echo "Publishing PhotoPrivacy.Ui + PhotoPrivacy.Worker ..."
echo "Runtime: ${RUNTIME} | Framework: ${FRAMEWORK}"

UI_PUB_DIR="${TARGET_DIR}/ui"
WORKER_PUB_DIR="${TARGET_DIR}/worker"
rm -rf "$TARGET_DIR"
mkdir -p "$UI_PUB_DIR" "$WORKER_PUB_DIR"

dotnet publish "${REPO_ROOT}/src/PhotoPrivacy.Ui/PhotoPrivacy.Ui.csproj" \
  -c Release \
  -f "$FRAMEWORK" \
  -r "$RUNTIME" \
  --self-contained "$SC" \
  /p:Version="$VERSION" \
  /p:UseAppHost=true \
  -o "$UI_PUB_DIR"

dotnet publish "${REPO_ROOT}/src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj" \
  -c Release \
  -f "$FRAMEWORK" \
  -r "$RUNTIME" \
  --self-contained "$SC" \
  /p:Version="$VERSION" \
  /p:UseAppHost=true \
  -o "$WORKER_PUB_DIR"

UI_EXE="PhotoPrivacy.Ui"
APP_EXE="PhotoPrivacy"
WORKER_EXE="PhotoPrivacyWorker"
if [[ "$IS_UNIX" == false ]]; then
  UI_EXE="PhotoPrivacy.Ui.exe"
  APP_EXE="PhotoPrivacy.exe"
  WORKER_EXE="PhotoPrivacyWorker.exe"
fi

[[ -f "${UI_PUB_DIR}/${UI_EXE}" ]] || { echo "UI executable not found: ${UI_PUB_DIR}/${UI_EXE}" >&2; exit 1; }
[[ -f "${WORKER_PUB_DIR}/${WORKER_EXE}" ]] || { echo "Worker executable not found: ${WORKER_PUB_DIR}/${WORKER_EXE}" >&2; exit 1; }

cp "${UI_PUB_DIR}/${UI_EXE}" "${TARGET_DIR}/${APP_EXE}"
cp "${WORKER_PUB_DIR}/${WORKER_EXE}" "${TARGET_DIR}/${WORKER_EXE}"

[[ -d "${UI_PUB_DIR}/Assets" ]] && cp -r "${UI_PUB_DIR}/Assets" "${TARGET_DIR}/Assets"

mkdir -p "${TARGET_DIR}/config"
cp "${REPO_ROOT}/config/config.sample.json" "${TARGET_DIR}/config/config.sample.json"
cp "${REPO_ROOT}/README.md" "${TARGET_DIR}/README.md"

[[ -f "${REPO_ROOT}/scripts/install-service.ps1" ]] && cp "${REPO_ROOT}/scripts/install-service.ps1" "${TARGET_DIR}/"
[[ -f "${REPO_ROOT}/scripts/install-systemd-service.sh" ]] && cp "${REPO_ROOT}/scripts/install-systemd-service.sh" "${TARGET_DIR}/"
[[ -f "${REPO_ROOT}/scripts/install-launchd-service.sh" ]] && cp "${REPO_ROOT}/scripts/install-launchd-service.sh" "${TARGET_DIR}/"

rm -rf "$UI_PUB_DIR" "$WORKER_PUB_DIR"

if [[ "$DO_ZIP" == true ]]; then
  if [[ "$IS_UNIX" == true ]]; then
    TARBALL="${RELEASE_ROOT}/PhotoPrivacy-${VERSION}-${RUNTIME}.tar.gz"
    rm -f "$TARBALL"
    tar -czf "$TARBALL" -C "$TARGET_DIR" .
    echo "Package created: $TARBALL"
  else
    echo "ZIP packaging on Windows runtime from Bash, skipping (use publish.ps1 on Windows for ZIP)" >&2
  fi
fi

echo "Publish done: $TARGET_DIR"
