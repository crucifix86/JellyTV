#!/usr/bin/env node
// Postinstall guard: electron's own install script uses the `extract-zip`
// npm package, which silently produces a partial extraction on newer Node
// runtimes (observed on Node 26 — only `dist/locales/` ends up written, no
// electron binary). When that happens, the npm-installed wrapper can't find
// the binary and `electron .` throws "Electron failed to install correctly".
//
// This script runs after npm install. If electron's marker files are present
// it's a no-op. If they're missing but the cached zip exists, fall back to
// the system `unzip` (which extracts correctly) and write the marker. If the
// zip isn't cached, re-run electron's own install.js to download it first.

const { execFileSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const electronDir = path.join(repoRoot, 'node_modules', 'electron');
const distDir = path.join(electronDir, 'dist');
const pathFile = path.join(electronDir, 'path.txt');

function log(msg) {
  console.log(`[ensure-electron] ${msg}`);
}

function isInstalled() {
  const binary = path.join(distDir, process.platform === 'win32' ? 'electron.exe' : 'electron');
  return fs.existsSync(binary) && fs.existsSync(pathFile);
}

function findCachedZip() {
  const cacheRoot = process.env.ELECTRON_CACHE
    || path.join(os.homedir(), '.cache', 'electron');
  if (!fs.existsSync(cacheRoot)) return null;

  for (const sub of fs.readdirSync(cacheRoot)) {
    const subPath = path.join(cacheRoot, sub);
    if (!fs.statSync(subPath).isDirectory()) continue;
    for (const file of fs.readdirSync(subPath)) {
      if (file.startsWith('electron-') && file.endsWith('.zip')) {
        return path.join(subPath, file);
      }
    }
  }
  return null;
}

function hasUnzip() {
  try {
    execFileSync('unzip', ['-v'], { stdio: 'ignore' });
    return true;
  } catch {
    return false;
  }
}

function extractWithUnzip(zipPath) {
  fs.mkdirSync(distDir, { recursive: true });
  execFileSync('unzip', ['-oq', zipPath, '-d', distDir], { stdio: 'inherit' });
}

function writeMarker() {
  const platformPath = process.platform === 'win32' ? 'electron.exe' : 'electron';
  fs.writeFileSync(pathFile, platformPath);

  const binary = path.join(distDir, platformPath);
  if (process.platform !== 'win32' && fs.existsSync(binary)) {
    fs.chmodSync(binary, 0o755);
    const sandbox = path.join(distDir, 'chrome-sandbox');
    if (fs.existsSync(sandbox)) fs.chmodSync(sandbox, 0o755);
  }
}

function runElectronInstall() {
  const installScript = path.join(electronDir, 'install.js');
  if (!fs.existsSync(installScript)) {
    throw new Error(`electron install.js not found at ${installScript}`);
  }
  execFileSync(process.execPath, [installScript], { stdio: 'inherit', cwd: electronDir });
}

function main() {
  if (!fs.existsSync(electronDir)) {
    log('electron package not present — npm install did not complete');
    process.exit(0);
  }

  if (isInstalled()) {
    return;
  }

  log('electron binary missing — extract-zip likely failed silently');

  let zipPath = findCachedZip();
  if (!zipPath) {
    log('no cached zip found, running electron install.js to download');
    try {
      runElectronInstall();
    } catch (err) {
      log(`electron install.js failed: ${err.message}`);
    }
    zipPath = findCachedZip();
  }

  if (isInstalled()) {
    return;
  }

  if (!zipPath) {
    log('no cached electron zip available — cannot recover');
    process.exit(1);
  }

  if (!hasUnzip()) {
    log('system `unzip` not available — install it and re-run `npm install`');
    process.exit(1);
  }

  log(`extracting ${path.basename(zipPath)} with system unzip`);
  extractWithUnzip(zipPath);
  writeMarker();

  if (!isInstalled()) {
    log('extraction completed but electron binary still missing — bailing out');
    process.exit(1);
  }

  log('electron ready');
}

main();
