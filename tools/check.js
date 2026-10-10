#!/usr/bin/env node

'use strict';

// Static checks of the repository. They never prove runtime behaviour (see docs/testing/RUNTIME_VALIDATION.md).
//  1. version: AppInfo.Version (src/Core.cs), the assembly attributes (src/Program.cs), the top CHANGELOG entry and the README agree;
//     the release ledger never claims a version newer than the sources.
//  2. every file of src/ is in tools/build.cmd and the other way round.
//  3. the sources are ASCII-only (user-facing strings are ASCII-only).
//  4. no binary or user data is tracked (exe, dll, wav, onnx, zip, piper/, voices/, backups/, settings json).
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const root = path.resolve(__dirname, '..');
const read = (p) => fs.readFileSync(path.join(root, p), 'utf8');
const errors = [];

function versionParts(v) { return v.split('.').map((n) => parseInt(n, 10)); }
function newer(a, b) {
  const x = versionParts(a), y = versionParts(b);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    const d = (x[i] || 0) - (y[i] || 0);
    if (d !== 0) return d > 0;
  }
  return false;
}

// 1. version
const core = read('src/Core.cs');
const program = read('src/Program.cs');
const sourceVersion = (core.match(/public const string Version = "([0-9]+\.[0-9]+)";/) || [])[1];
if (!sourceVersion) errors.push('AppInfo.Version not found in src/Core.cs');
else {
  const asm = program.match(/AssemblyVersion\("([0-9.]+)"\)/);
  const asmFile = program.match(/AssemblyFileVersion\("([0-9.]+)"\)/);
  if (!asm || asm[1] !== `${sourceVersion}.0.0`) errors.push(`AssemblyVersion must be ${sourceVersion}.0.0 (found ${asm ? asm[1] : 'none'})`);
  if (!asmFile || asmFile[1] !== `${sourceVersion}.0.0`) errors.push(`AssemblyFileVersion must be ${sourceVersion}.0.0 (found ${asmFile ? asmFile[1] : 'none'})`);
  const log = read('CHANGELOG.en.md');
  const top = (log.match(/^## Release ([0-9]+\.[0-9]+)/m) || [])[1];
  if (top !== sourceVersion) errors.push(`top CHANGELOG entry is ${top || 'missing'}, the sources are ${sourceVersion}`);
  const readme = read('README.md');
  if (!readme.includes(`Version ${sourceVersion}`)) errors.push(`README.md must state "Version ${sourceVersion}"`);
  const ledger = JSON.parse(read('tools/release-ledger.json'));
  if (newer(ledger.lastDeliveredVersion, sourceVersion)) errors.push(`release ledger (${ledger.lastDeliveredVersion}) is newer than the sources (${sourceVersion})`);
}

// 2. build list
const build = read('tools/build.cmd');
const listed = [...build.matchAll(/src\\([A-Za-z]+\.cs)/g)].map((m) => m[1]);
const present = fs.readdirSync(path.join(root, 'src')).filter((f) => f.endsWith('.cs'));
for (const f of present) if (!listed.includes(f)) errors.push(`src/${f} is not compiled by tools/build.cmd`);
for (const f of listed) if (!present.includes(f)) errors.push(`tools/build.cmd compiles src/${f}, which does not exist`);

// 3. ASCII
for (const f of present) {
  const text = read(path.join('src', f));
  const m = text.match(/[^\x00-\x7F]/);
  if (m) errors.push(`src/${f} has a non-ASCII character (${JSON.stringify(m[0])})`);
}

// 4. tracked files
try {
  const tracked = cp.execFileSync('git', ['-C', root, 'ls-files'], { encoding: 'utf8' }).split(/\r?\n/).filter(Boolean);
  const bad = tracked.filter((f) => /\.(exe|dll|wav|onnx|zip|pdb)$/i.test(f) || /^(piper|voices|backups|audio|bin|old)\//i.test(f) || /^HPGMissionCompanion\.json$/i.test(f));
  for (const f of bad) errors.push(`${f} must not be tracked (binary or user data)`);
} catch (e) {
  // not a git checkout (for example a source archive): skip
}

if (errors.length) {
  console.error('Checks FAIL:');
  for (const e of errors) console.error(' - ' + e);
  process.exit(1);
}
console.log(`Checks PASS (version ${sourceVersion}, ${present.length} source files).`);
