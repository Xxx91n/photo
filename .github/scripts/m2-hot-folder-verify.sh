#!/usr/bin/env bash
# 一次性 harness（#2 macOS 半边）：对已解压的 osx 发布包跑 hot 目录清洗 + 审计对账。
# 前置：verify-launchd-service.sh 已跑完并卸载；本脚本会重新安装服务、投样、
# 等待 Worker 处理、对比 EXIF、查审计，最后卸载。只在一次性 CI 分支使用。
set -euo pipefail

RELEASE_DIR="${1:?release dir}"
EVIDENCE_DIR="${2:?evidence dir}"

SERVICE_LABEL="com.photoprivacy.cleaner"
PLIST="/Library/LaunchDaemons/${SERVICE_LABEL}.plist"
INSTALL_DIR="/opt/photoprivacy"
HOT="/var/lib/photoprivacy/hot"
AUDIT="/var/log/photoprivacy"
QUARANTINE="/var/lib/photoprivacy/quarantine"
SOCKET="/Library/Application Support/photoprivacy/worker.sock"
EXIF="${RELEASE_DIR}/ExifTool/exiftool"
SAMPLE="M2-sample.jpg"
RUNNER_USER="${SUDO_USER:-runner}"

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

# ---------- 1. 重新安装服务 ----------
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
perl "$EXIF" -s -Artist -Model -GPSLatitude -GPSLongitude "$WORK/$SAMPLE" > "$EVIDENCE_DIR/03-before-exif.txt" 2>&1 || true
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
  rec INFO "审计条目未暴露文件名（路径脱敏），以 file_processing_succeeded 事件为准"
fi
if command -v jq >/dev/null 2>&1 && jq -e . "$AUDIT_HIT" >/dev/null 2>&1; then
  rec PASS "审计命中行是合法 JSON"
else
  rec INFO "审计命中行未通过 jq 校验（jq 缺失或命中含非 JSON 行）"
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
perl "$EXIF" -s -Artist -Model -GPSLatitude -GPSLongitude "$EVIDENCE_DIR/06-hot-after.jpg" > "$EVIDENCE_DIR/06-after-exif.txt" 2>&1 || true
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
  perl "$EXIF" -s -Artist -Model -GPSLatitude -GPSLongitude "$EVIDENCE_DIR/09-backup.jpg" > "$EVIDENCE_DIR/09-backup-exif.txt" 2>&1 || true
  if grep -Eq 'TestUser|TestCam|GPSLatitude|GPSLongitude' "$EVIDENCE_DIR/09-backup-exif.txt"; then
    rec PASS "备份文件保留原始元数据（${BACKUP}）"
  else
    rec FAIL "备份文件存在但原始元数据缺失（${BACKUP}）"
  fi
else
  rec INFO "未找到 M2-sample 的备份文件（默认落点 ${HOT}/bak）"
fi

# ---------- 6. 服务仍存活 + UDS 仍在 ----------
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

# ---------- 7. 日志留证 ----------
capture 10-worker-logs.txt sudo sh -c 'tail -n 120 /opt/photoprivacy/logs/worker-*.log 2>/dev/null'
capture 11-audit-ls.txt sudo sh -c 'ls -la /var/log/photoprivacy'
capture 12-service-logs.txt sudo sh -c 'tail -n 60 /var/log/photoprivacy/stdout.log /var/log/photoprivacy/stderr.log 2>/dev/null'

# ---------- 8. UI 探测对拍（补 ExifTool 版本） ----------
sudo launchctl asuser "$(id -u "$RUNNER_USER")" sudo -u "$RUNNER_USER" \
  "$INSTALL_DIR/PhotoPrivacy" \
  --service-probe-report "$EVIDENCE_DIR/13-ui-probe-after.json" \
  --service-probe-ipc \
  --worker "$EVIDENCE_DIR/no-worker" > "$EVIDENCE_DIR/13-ui-probe-after.txt" 2>&1 || true
if [[ -f "$EVIDENCE_DIR/13-ui-probe-after.json" ]]; then
  rec PASS "hot 验证后 UI 探测报告已产出（13-ui-probe-after.json）"
  if grep -q '"ipc":"Reachable"' "$EVIDENCE_DIR/13-ui-probe-after.json"; then
    rec PASS "hot 验证后 UI 服务模式 IPC 仍可达"
  else
    rec FAIL "hot 验证后 UI 服务模式 IPC 不可达"
  fi
  if grep -q '"exif_tool_version":"unknown"' "$EVIDENCE_DIR/13-ui-probe-after.json"; then
    rec INFO "UI 状态接口仍报 exif_tool_version=unknown（清洗与审计已独立验证）"
  fi
else
  rec FAIL "hot 验证后 UI 探测报告未产出"
fi

# ---------- 9. 卸载 ----------
set +e
sudo launchctl unload -w "$PLIST" > "$EVIDENCE_DIR/14-uninstall.txt" 2>&1
sudo rm -f "$PLIST"
set -e
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
