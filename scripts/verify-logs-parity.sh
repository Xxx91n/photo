#!/usr/bin/env bash
# verify-logs-parity.sh —— #57 日志页对齐与审计尾巴双通道虚拟环境验证脚本。
#
# 目的：在远端 CI 虚拟环境（如 GitHub Actions runner / Linux 容器）下模拟
# 大批量日志流灌入（超出 500 条上限）、双通道（IPC backfill + 本地 tail）协调、
# 滚动与过滤等效性断言，提供机器可读与留证据产物。
#
# 用法:
#   ./scripts/verify-logs-parity.sh --evidence-dir <dir> [--count <n>]
#
set -euo pipefail

EVIDENCE_DIR=""
LOG_COUNT=600

usage() {
  cat <<EOF
Usage: $0 --evidence-dir <dir> [--count <n>]

Options:
  --evidence-dir <dir>  验证证据与日志输出目录
  --count <n>           大批量日志条数（默认 600，验证 500 条截断上限）
  -h, --help            Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release-dir) shift 2 ;; # 兼容旧参数
    --evidence-dir) EVIDENCE_DIR="${2:-}"; shift 2 ;;
    --count) LOG_COUNT="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -z "$EVIDENCE_DIR" ]]; then
  echo "Missing required arguments: --evidence-dir is required" >&2
  usage
  exit 1
fi

if ! [[ "$LOG_COUNT" =~ ^[0-9]+$ ]] || [ "$LOG_COUNT" -le 500 ]; then
  echo "Error: --count must be a positive integer greater than 500 (MAX_LOG_ENTRIES) to verify truncation" >&2
  exit 1
fi

mkdir -p "$EVIDENCE_DIR"
SUMMARY_FILE="${EVIDENCE_DIR}/summary.txt"
AUDIT_DIR="${EVIDENCE_DIR}/audit"
mkdir -p "$AUDIT_DIR"

echo "=== #57 日志页双通道与虚拟环境验证 ===" | tee "$SUMMARY_FILE"
echo "Date: $(date -u '+%Y-%m-%d %H:%M:%SZ')" | tee -a "$SUMMARY_FILE"
echo "Evidence dir: $EVIDENCE_DIR" | tee -a "$SUMMARY_FILE"

# 1. 模拟大批量审计日志文件写入（本地日期基准）
TODAY=$(date '+%Y-%m-%d')
AUDIT_FILE="$(CDPATH= cd -- "$AUDIT_DIR" && pwd)/audit-${TODAY}.jsonl"

# 运行前截断旧文件，确保多次运行不会导致条目累加
: > "$AUDIT_FILE"

echo "[Step 1] 生成大批量日志数据: ${LOG_COUNT} 条 -> ${AUDIT_FILE}" | tee -a "$SUMMARY_FILE"
START_TS=$(date +%s)
BASE_EPOCH=1793664000  # 2026-11-03T00:00:00Z
if date -u -d "@0" '+%s' >/dev/null 2>&1; then
  format_utc() { date -u -d "@$1" '+%Y-%m-%dT%H:%M:%SZ'; }
else
  format_utc() { date -u -r "$1" '+%Y-%m-%dT%H:%M:%SZ'; }
fi

for i in $(seq 1 "$LOG_COUNT"); do
  TIMESTAMP_UTC=$(format_utc $((BASE_EPOCH + i)))
  cat <<EOF >> "$AUDIT_FILE"
{"timestamp_utc":"${TIMESTAMP_UTC}","level":"Information","event_type":"file_cleaned","message":"Cleaned photo batch item ${i}","source_path_masked":"/fake/photo_${i}.jpg","wipe_result":"Cleaned_Modified"}
EOF
done
GEN_DUR=$(($(date +%s) - START_TS))
echo "生成完成，用时 ${GEN_DUR}s，文件大小: $(wc -c < "$AUDIT_FILE") 字节" | tee -a "$SUMMARY_FILE"

# 2. 真实执行 Rust 测试验证 LogBuffer 对大批量注入的截断与容量管理
echo "[Step 2] 执行测试验证 LogBuffer 500 条截断上限与双通道协调" | tee -a "$SUMMARY_FILE"
if command -v cargo >/dev/null 2>&1; then
  export AUDIT_TEST_FILE="$AUDIT_FILE"
  (cd ui && cargo test -p photoprivacy-logs -- --nocapture) | tee -a "$SUMMARY_FILE"
  echo "PASS: cargo test -p photoprivacy-logs 验证通过" | tee -a "$SUMMARY_FILE"
else
  CARGO_SKIPPED=1
  echo "WARN: 本地未预装 cargo，交由 CI 执行该测试" | tee -a "$SUMMARY_FILE"
fi

echo "=== 验证结果汇总 ===" | tee -a "$SUMMARY_FILE"
if [[ "${CARGO_SKIPPED:-0}" -eq 1 ]]; then
  echo "SKIP: 日志生成完成；LogBuffer 容量测试未执行（cargo 不可用）" | tee -a "$SUMMARY_FILE"
else
  echo "PASS: 大批量日志注入与容量验证完成" | tee -a "$SUMMARY_FILE"
fi
exit 0
