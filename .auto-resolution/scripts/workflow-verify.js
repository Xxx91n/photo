#!/usr/bin/env node
'use strict';
/**
 * workflow-verify.js — WORKFLOW §4.4 轨 2：快照零丢失校验（票14 转正版）。
 *
 * 用法：node scripts/workflow-verify.js <快照目录> [源目录]
 *   快照目录：含 manifest.json 的快照根（workflow-snapshot.js 产物）。
 *   源目录缺省：manifest.source 字段。
 * 行为：现树 vs 快照 manifest 逐文件（字节数 + SHA256）比对，
 *   输出 missing / changed / added 明细与 ZERO-LOSS 判定。
 *   missing 或 changed > 0 => LOSS DETECTED，退出码 1；
 *   added 单列提示（新增内容不属丢失，不算损失）。
 * 仅用 Node 内置 fs/path/crypto，无子进程、无网络。
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

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

function main() {
  const snapshotDir = path.resolve(process.argv[2] || '');
  if (!process.argv[2]) {
    console.error('[verify] 用法: node scripts/workflow-verify.js <快照目录> [源目录]');
    process.exit(2);
  }
  const manifestPath = path.join(snapshotDir, 'manifest.json');
  if (!fs.existsSync(manifestPath)) {
    console.error('[verify] manifest.json 不存在: ' + manifestPath);
    process.exit(2);
  }
  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  const sourceDir = path.resolve(process.argv[3] || manifest.source);

  const current = new Map();
  if (fs.existsSync(sourceDir) && fs.statSync(sourceDir).isDirectory()) {
    for (const abs of listFiles(sourceDir)) {
      current.set(path.relative(sourceDir, abs).split(path.sep).join('/'), abs);
    }
  } else {
    console.warn('[verify] 警告: 现树目录不存在: ' + sourceDir + '（若路径无误，整树丢失按 LOSS 处置）');
  }

  const missing = [];
  const changed = [];
  let matched = 0;
  for (const entry of manifest.files) {
    const abs = current.get(entry.file);
    if (!abs) { missing.push(entry.file); continue; }
    current.delete(entry.file);
    let ok = false;
    try {
      ok = fs.statSync(abs).size === entry.bytes && sha256File(abs) === entry.sha256;
    } catch (err) { ok = false; }
    if (ok) matched++; else changed.push(entry.file);
  }
  const added = Array.from(current.keys()).sort();

  for (const m of missing) console.log('MISSING  ' + m);
  for (const c of changed) console.log('CHANGED  ' + c);
  for (const a of added) console.log('ADDED    ' + a);

  const zeroLoss = missing.length === 0 && changed.length === 0;
  if (zeroLoss) {
    console.log('ZERO-LOSS ' + matched + '/' + manifest.fileCount + ' 哈希一致');
    if (added.length > 0) {
      console.log('[verify] 另有 ' + added.length + ' 个新增文件（不属丢失，未计入判定）。');
    }
    process.exit(0);
  }
  console.log('LOSS DETECTED: missing=' + missing.length + ' changed=' + changed.length + ' added=' + added.length);
  process.exit(1);
}

try {
  main();
} catch (err) {
  console.error('[verify] 失败: ' + (err && err.message ? err.message : err));
  process.exit(2);
}
