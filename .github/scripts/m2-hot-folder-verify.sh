#!/usr/bin/env bash
# M2 = GitHub issue #2 macOS half evidence group.
# Standalone hot-folder harness: stage the release, reinstall the service, drop a
# JPEG with EXIF, wait for the Worker, check audit/backup/service/UDS, then
# uninstall. It calls the release artifact's install script and mirrors the
# install-launchd-service.sh #2 paths; it does not source verify-launchd-service.sh
# because that script is a one-shot lifecycle harness, not a function library.
# Compatible with macOS bash 3.2; uses macOS base64 -D, sips, find -print -quit.
set -euo pipefail

GUI_USER=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --gui-user)
      if [[ -z "${2:-}" || -z "${2//[[:space:]]/}" ]]; then
        echo "Error: --gui-user requires a non-empty GUI user name." >&2
        exit 2
      fi
      GUI_USER="$2"
      shift 2
      ;;
    --*)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
    *)
      break
      ;;
  esac
done

RELEASE_DIR="${1:?release dir}"
EVIDENCE_DIR="${2:?evidence dir}"
if [[ -z "$GUI_USER" || "$GUI_USER" == "root" ]]; then
  echo "Usage: $0 --gui-user <GUI user> <release dir> <evidence dir>" >&2
  exit 2
fi
if ! id -u "$GUI_USER" >/dev/null 2>&1; then
  echo "GUI user does not exist: $GUI_USER" >&2
  exit 2
fi
RUNNER_USER="$GUI_USER"

# These mirror install-launchd-service.sh's #2 defaults; keep them in sync.
SERVICE_LABEL="com.photoprivacy.cleaner"
PLIST="/Library/LaunchDaemons/${SERVICE_LABEL}.plist"
INSTALL_DIR="/opt/photoprivacy"
HOT="/var/lib/photoprivacy/hot"
AUDIT="/var/log/photoprivacy"
QUARANTINE="/var/lib/photoprivacy/quarantine"
SOCKET="/Library/Application Support/photoprivacy/worker.sock"
EXIF="${RELEASE_DIR}/ExifTool/exiftool"
SAMPLE="M2-sample.jpg"

mkdir -p "$EVIDENCE_DIR"
RESULT="$EVIDENCE_DIR/result.txt"
: > "$RESULT"
PASS=0
FAIL=0

rec() {
  local status="$1"
  shift
  printf '%s  %s\n' "$status" "$*" | tee -a "$RESULT"
  case "$status" in
    PASS) PASS=$((PASS + 1)) ;;
    FAIL) FAIL=$((FAIL + 1)) ;;
    INFO) ;;
  esac
}

finish() {
  {
    echo "---- 结论 ----"
    echo "PASS=$PASS FAIL=$FAIL"
  } | tee -a "$RESULT"
  if [[ "$FAIL" -gt 0 ]]; then
    exit 1
  fi
}

fail() {
  rec FAIL "$1"
  finish
}

capture() {
  local file="$1"
  shift
  local rc=0
  {
    echo "\$ $*"
    "$@" 2>&1 || rc=$?
    echo "[exit ${rc}]"
  } >> "$EVIDENCE_DIR/$file"
  return 0
}

wait_running() {
  local deadline=$((SECONDS + 60))
  while (( SECONDS < deadline )); do
    if sudo launchctl print "system/${SERVICE_LABEL}" 2>/dev/null | grep -Eq 'pid = [0-9]+'; then
      return 0
    fi
    sleep 1
  done
  return 1
}

# ---------- 0. 前置：perl + 内置 ExifTool ----------
capture 00-perl.txt bash -c 'command -v perl; ls -l /usr/bin/perl 2>&1; perl -v 2>&1 | head -3'
if ! command -v perl >/dev/null 2>&1; then
  fail "perl 不可用——内置 ExifTool 无法运行（发布包要求目标机提供 perl）"
fi
if ! perl "$EXIF" -ver > "$EVIDENCE_DIR/00-exiftool-version.txt" 2>&1; then
  cat "$EVIDENCE_DIR/00-exiftool-version.txt" >> "$RESULT" || true
  fail "内置 ExifTool -ver 失败"
fi
rec PASS "内置 ExifTool 可运行（$(tr -d '\r\n' < "$EVIDENCE_DIR/00-exiftool-version.txt")）"

# ---------- 1. staging + install ----------
capture 00-staging.txt sudo bash -c "mkdir -p '$INSTALL_DIR' && cp -R '$RELEASE_DIR/.' '$INSTALL_DIR/' && chmod +x '$INSTALL_DIR/PhotoPrivacyWorker' '$INSTALL_DIR/PhotoPrivacy'"
if [[ ! -f "$INSTALL_DIR/PhotoPrivacyWorker" ]]; then
  fail "staging 失败：$INSTALL_DIR/PhotoPrivacyWorker 不存在"
fi
rec PASS "发布包已铺到 $INSTALL_DIR"

sudo launchctl unload -w "$PLIST" >/dev/null 2>&1 || true
sudo rm -f "$PLIST"
if ! sudo bash "$RELEASE_DIR/scripts/install-launchd-service.sh" \
  --install-dir "$INSTALL_DIR" \
  --config "$INSTALL_DIR/config/config.json" \
  --hot-folder "$HOT" \
  --audit-folder "$AUDIT" \
  --quarantine "$QUARANTINE" \
  --user photoprivacy \
  --group photoprivacy \
  --socket-group photoprivacy-ipc \
  --gui-user "$RUNNER_USER" > "$EVIDENCE_DIR/01-install.txt" 2>&1; then
  cat "$EVIDENCE_DIR/01-install.txt" >> "$RESULT" || true
  fail "install-launchd-service.sh 执行失败"
fi
if ! wait_running; then
  sudo launchctl print "system/${SERVICE_LABEL}" > "$EVIDENCE_DIR/01-launchctl.txt" 2>&1 || true
  fail "安装后 60s 内 Worker 未进入 Running"
fi
rec PASS "服务已重新安装并运行（launchctl print system/${SERVICE_LABEL} 有 PID）"
sleep 3

# ---------- 2. 造带 EXIF 的样本 ----------
WORK="$EVIDENCE_DIR/work"
mkdir -p "$WORK"
if ! printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n0sAAAAASUVORK5CYII=' | base64 -D > "$WORK/pixel.png"; then
  fail "base64 生成 PNG 失败"
fi
# 实测：sips 把 1x1 PNG 直接转 JPEG 会产出 ExifTool 判为 Corrupted 的文件。
# 先放大到 64x64；仍不可解析就退回系统自带图片。
sips -z 64 64 "$WORK/pixel.png" --out "$WORK/pixel64.png" >> "$EVIDENCE_DIR/02-sips.txt" 2>&1 || true
SYSTEM_IMG="$(find "/System/Library/Desktop Pictures" /System/Library/CoreServices -maxdepth 2 -type f \( -iname '*.heic' -o -iname '*.jpg' -o -iname '*.png' \) -print -quit 2>/dev/null || true)"
make_sample() {
  local src="$1"
  rm -f "$WORK/$SAMPLE"
  sips -s format jpeg "$src" --out "$WORK/$SAMPLE" >> "$EVIDENCE_DIR/02-sips.txt" 2>&1 || return 1
  perl "$EXIF" -s "$WORK/$SAMPLE" >/dev/null 2>&1 || return 1
  return 0
}
if ! make_sample "$WORK/pixel64.png"; then
  if [[ -n "$SYSTEM_IMG" ]] && make_sample "$SYSTEM_IMG"; then
    :
  else
    cat "$EVIDENCE_DIR/02-sips.txt" >> "$RESULT" || true
    fail "sips 生成的 JPEG 无法被 ExifTool 解析（64x64 与系统图片回退均失败）"
  fi
fi
if ! perl "$EXIF" -overwrite_original \
  -Artist=TestUser \
  -Model=TestCam \
  -GPSLatitude=39.9042 -GPSLatitudeRef=N \
  -GPSLongitude=116.4074 -GPSLongitudeRef=E \
  "$WORK/$SAMPLE" > "$EVIDENCE_DIR/02-exif-write.txt" 2>&1; then
  cat "$EVIDENCE_DIR/02-exif-write.txt" >> "$RESULT" || true
  fail "ExifTool 写入 EXIF 失败"
fi
if ! perl "$EXIF" -s -Artist -Model -GPSLatitude -GPSLongitude "$WORK/$SAMPLE" > "$EVIDENCE_DIR/03-before-exif.txt" 2>&1; then
  fail "样本 ExifTool 读取失败"
fi
if ! grep -q 'TestUser' "$EVIDENCE_DIR/03-before-exif.txt" \
  || ! grep -q 'TestCam' "$EVIDENCE_DIR/03-before-exif.txt" \
  || ! grep -q 'GPSLatitude' "$EVIDENCE_DIR/03-before-exif.txt" \
  || ! grep -q 'GPSLongitude' "$EVIDENCE_DIR/03-before-exif.txt"; then
  cat "$EVIDENCE_DIR/03-before-exif.txt" >> "$RESULT" || true
  fail "样本 EXIF 未写全（Artist/Model/GPS 至少一项缺失）"
fi
rec PASS "样本已写入 EXIF（Artist=TestUser / Model=TestCam / GPS）"

# ---------- 3. 投进 hot 目录 ----------
sudo cp "$WORK/$SAMPLE" "$HOT/$SAMPLE"
sudo chown photoprivacy:photoprivacy "$HOT/$SAMPLE"
sudo chmod 0644 "$HOT/$SAMPLE"
capture 04-hot-before.txt sudo ls -la "$HOT/$SAMPLE"

# ---------- 4. 等审计出现 file_processing_succeeded ----------
AUDIT_HIT="$EVIDENCE_DIR/05-audit-hit.jsonl"
: > "$AUDIT_HIT"
deadline=$((SECONDS + 180))
while (( SECONDS < deadline )); do
  sudo sh -c "cat /var/log/photoprivacy/audit-*.jsonl 2>/dev/null | grep -E 'M2-sample|file_processing_succeeded' > '$AUDIT_HIT'" || true
  if grep -q 'file_processing_succeeded' "$AUDIT_HIT"; then
    break
  fi
  sleep 2
done
if ! grep -q 'file_processing_succeeded' "$AUDIT_HIT"; then
  capture 05-audit-now.txt sudo sh -c 'ls -la /var/log/photoprivacy; tail -n 40 /var/log/photoprivacy/audit-*.jsonl 2>/dev/null'
  fail "180s 内审计未出现 file_processing_succeeded"
fi
rec PASS "审计出现 file_processing_succeeded"
if grep -q 'M2-sample' "$AUDIT_HIT"; then
  rec PASS "审计条目能对上 M2-sample.jpg"
else
  rec FAIL "审计条目没有暴露 M2-sample.jpg，无法和样本对上"
fi
if command -v jq >/dev/null 2>&1; then
  if jq -e . "$AUDIT_HIT" >/dev/null 2>&1; then
    rec PASS "审计命中行是合法 JSON"
  else
    rec FAIL "审计命中行不是合法 JSON"
  fi
else
  rec INFO "jq 不可用，跳过审计 JSON 解析"
fi

# ---------- 5. 清洗后 EXIF + 备份 ----------
if ! sudo test -f "$HOT/$SAMPLE"; then
  capture 06-hot-missing.txt sudo sh -c "find /var/lib/photoprivacy -name 'M2-sample*' -print"
  fail "hot 目录里找不到清洗后的 $SAMPLE"
fi
if ! sudo cp -f "$HOT/$SAMPLE" "$EVIDENCE_DIR/06-hot-after.jpg"; then
  fail "复制清洗后的 $SAMPLE 到证据目录失败"
fi
sudo chown "$RUNNER_USER" "$EVIDENCE_DIR/06-hot-after.jpg" 2>/dev/null || true
if ! perl "$EXIF" -s -Artist -Model -GPSLatitude -GPSLongitude "$EVIDENCE_DIR/06-hot-after.jpg" > "$EVIDENCE_DIR/06-after-exif.txt" 2>&1; then
  fail "清洗后 ExifTool 读取失败（不能把读取失败当作元数据已清空）"
fi
if grep -Eq 'TestUser|TestCam|GPSLatitude|GPSLongitude' "$EVIDENCE_DIR/06-after-exif.txt"; then
  cat "$EVIDENCE_DIR/06-after-exif.txt" >> "$RESULT" || true
  fail "hot 文件仍有 Artist/Model/GPS 元数据"
fi
rec PASS "hot 文件元数据已清空（Artist/Model/GPS 均无输出）"
capture 07-hot-after.txt sudo ls -la "$HOT/$SAMPLE"

BACKUP="$(sudo find /var/lib/photoprivacy -type f -name "${SAMPLE}*" 2>/dev/null | grep -v "^${HOT}/${SAMPLE}$" | head -n 1 || true)"
if [[ -n "$BACKUP" ]]; then
  printf '%s\n' "$BACKUP" > "$EVIDENCE_DIR/08-backup-path.txt"
  if ! sudo cp -f "$BACKUP" "$EVIDENCE_DIR/09-backup.jpg"; then
    fail "复制备份文件到证据目录失败（${BACKUP}）"
  fi
  sudo chown "$RUNNER_USER" "$EVIDENCE_DIR/09-backup.jpg" 2>/dev/null || true
  if ! perl "$EXIF" -s -Artist -Model -GPSLatitude -GPSLongitude "$EVIDENCE_DIR/09-backup.jpg" > "$EVIDENCE_DIR/09-backup-exif.txt" 2>&1; then
    fail "备份文件 ExifTool 读取失败（${BACKUP}）"
  fi
  if grep -Eq 'TestUser|TestCam|GPSLatitude|GPSLongitude' "$EVIDENCE_DIR/09-backup-exif.txt"; then
    rec PASS "备份文件保留原始元数据（${BACKUP}）"
  else
    rec FAIL "备份文件存在但原始元数据缺失（${BACKUP}）"
  fi
else
  rec FAIL "未找到 M2-sample 的备份文件（默认落点 ${HOT}/bak）"
fi

# ---------- 6. 日志和审计同会话 ----------
AUDIT_TS="$(sed -n 's/.*"timestamp_utc":"\([^"]*\)".*/\1/p' "$AUDIT_HIT" | head -n 1)"
WORKER_LOG="$(sudo sh -c "ls -t '$INSTALL_DIR'/logs/worker-*.log 2>/dev/null | head -n 1")"
WORKER_START=""
if [[ -n "$WORKER_LOG" ]]; then
  WORKER_START="$(sudo grep -h 'Application started' "$WORKER_LOG" | tail -n 1 | awk '{print $1" "$2}')"
fi
if [[ -n "$AUDIT_TS" && -n "$WORKER_START" ]]; then
  AUDIT_EPOCH="$(date -j -u -f '%Y-%m-%dT%H:%M:%S' "${AUDIT_TS:0:19}" +%s 2>/dev/null || true)"
  WORKER_EPOCH="$(date -j -u -f '%Y-%m-%d %H:%M:%S' "${WORKER_START%.*}" +%s 2>/dev/null || true)"
  if [[ -n "$AUDIT_EPOCH" && -n "$WORKER_EPOCH" && "$AUDIT_EPOCH" -ge "$WORKER_EPOCH" ]]; then
    rec PASS "审计时间落在当前 Worker 会话内（audit ${AUDIT_TS} >= worker ${WORKER_START}）"
  else
    rec FAIL "审计时间与 Worker 启动对不上（audit=${AUDIT_TS:-<空>} worker=${WORKER_START:-<空>}）"
  fi
else
  rec FAIL "取不到审计/Worker 日志时间（audit=${AUDIT_TS:-<空>} worker=${WORKER_START:-<空>}）"
fi

# ---------- 7. 服务仍存活 + UDS 仍在 ----------
if sudo launchctl print "system/${SERVICE_LABEL}" 2>/dev/null | grep -Eq 'pid = [0-9]+'; then
  rec PASS "清洗后服务仍运行中"
else
  rec FAIL "清洗后服务不再运行"
fi
if [[ -S "$SOCKET" ]]; then
  rec PASS "清洗后服务 UDS 仍在（${SOCKET}）"
else
  rec FAIL "清洗后服务 UDS 不见了（${SOCKET}）"
fi

# ---------- 8. 日志留证 ----------
capture 10-worker-logs.txt sudo sh -c 'tail -n 120 /opt/photoprivacy/logs/worker-*.log 2>/dev/null'
capture 11-audit-ls.txt sudo sh -c 'ls -la /var/log/photoprivacy'
capture 12-service-logs.txt sudo sh -c 'tail -n 60 /var/log/photoprivacy/stdout.log /var/log/photoprivacy/stderr.log 2>/dev/null'

# ---------- 9. 卸载 ----------
set +e
sudo launchctl unload -w "$PLIST" > "$EVIDENCE_DIR/14-uninstall.txt" 2>&1
UNLOAD_RC=$?
sudo rm -f "$PLIST"
set -e
echo "[unload exit ${UNLOAD_RC}]" >> "$EVIDENCE_DIR/14-uninstall.txt"
if [[ "$UNLOAD_RC" -eq 0 ]]; then
  rec PASS "卸载 unload 成功（exit 0）"
else
  rec FAIL "卸载 unload 失败（exit ${UNLOAD_RC}）"
fi
i=0
while [[ $i -lt 15 ]]; do
  if ! pgrep -f "$INSTALL_DIR/PhotoPrivacyWorker" >/dev/null 2>&1; then
    break
  fi
  sleep 1
  i=$((i + 1))
done
if [[ ! -f "$PLIST" ]] && ! pgrep -f "$INSTALL_DIR/PhotoPrivacyWorker" >/dev/null 2>&1; then
  rec PASS "hot 验证后卸载干净（plist 已删、Worker 无残留）"
else
  rec FAIL "hot 验证后卸载不干净（plist 或 Worker 残留）"
fi

finish
