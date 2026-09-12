#!/usr/bin/env node
'use strict';
/**
 * workflow-snapshot.js — WORKFLOW §4.4 轨 2：流程产物仓库外快照（票14 转正版）。
 *
 * 用法：node scripts/workflow-snapshot.js [源目录] [输出根]
 *   源目录缺省： <仓库根>/.scratch/architecture-recovery
 *   输出根缺省： <仓库根>/../photo-snapshots
 * 行为：整树复制源目录到 <输出根>/<yyyyMMdd-HHmmss>/，并写 manifest.json
 *   （逐文件字节数 + SHA256），快照完成前禁止动栈（WORKFLOW §4.4）。
 * 配套校验：scripts/workflow-verify.js
 * 仅用 Node 内置 fs/path/crypto，无子进程、无网络。
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const REPO_ROOT = path.resolve(__dirname, '..');

function sha256File(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}

function listFiles(rootDir) {
  const out = [];
  const walk = (dir) => {
    const entries = fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => (a.name < b.name ? -1 : 1));
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile()) out.push(full);
    }
  };
  walk(rootDir);
  return out;
}

function pad2(n) { return String(n).padStart(2, '0'); }

function main() {
  const sourceDir = path.resolve(process.argv[2] || path.join(REPO_ROOT, '.scratch', 'architecture-recovery'));
  const destRoot = path.resolve(process.argv[3] || path.join(REPO_ROOT, '..', 'photo-snapshots'));

  if (!fs.existsSync(sourceDir) || !fs.statSync(sourceDir).isDirectory()) {
    console.error('[snapshot] 源目录不存在或不是目录: ' + sourceDir);
    process.exit(2);
  }

  const now = new Date();
  const stamp = now.getFullYear() + pad2(now.getMonth() + 1) + pad2(now.getDate()) +
    '-' + pad2(now.getHours()) + pad2(now.getMinutes()) + pad2(now.getSeconds());
  const snapshotDir = path.join(destRoot, stamp);
  fs.mkdirSync(snapshotDir, { recursive: true });

  const files = [];
  let totalBytes = 0;
  for (const abs of listFiles(sourceDir)) {
    const rel = path.relative(sourceDir, abs).split(path.sep).join('/');
    const target = path.join(snapshotDir, rel);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(abs, target);
    const bytes = fs.statSync(abs).size;
    const digest = sha256File(abs);
    files.push({ file: rel, bytes: bytes, sha256: digest });
    totalBytes += bytes;
  }

  const manifest = {
    snapshot: snapshotDir,
    takenAt: new Date().toISOString(),
    source: sourceDir.split(path.sep).join('/'),
    fileCount: files.length,
    totalBytes: totalBytes,
    files: files
  };
  fs.writeFileSync(path.join(snapshotDir, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8');

  console.log('[snapshot] ' + files.length + ' 文件 / ' + totalBytes + ' 字节 -> ' + snapshotDir);
  console.log('[snapshot] manifest.json 已写入（逐文件字节数 + SHA256）。动栈前请留存本目录。');
}

try {
  main();
} catch (err) {
  console.error('[snapshot] 失败: ' + (err && err.message ? err.message : err));
  console.error('[snapshot] 若为"文件在快照中途消失"，请直接重跑本脚本。');
  process.exit(2);
}
