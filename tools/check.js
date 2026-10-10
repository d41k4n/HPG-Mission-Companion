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

// installer: same version as the program, compiled by build.cmd, ASCII
const setupPath = path.join(root, 'setup', 'Setup.cs');
if (!fs.existsSync(setupPath)) errors.push('setup/Setup.cs is missing');
else {
  const setup = read('setup/Setup.cs');
  const sv = (setup.match(/public const string Version = "([0-9]+\.[0-9]+)";/) || [])[1];
  if (sv !== sourceVersion) errors.push(`setup/Setup.cs Version is ${sv || 'missing'}, the sources are ${sourceVersion}`);
  const sa = setup.match(/AssemblyVersion\("([0-9.]+)"\)/);
  if (!sa || sa[1] !== `${sourceVersion}.0.0`) errors.push(`setup/Setup.cs AssemblyVersion must be ${sourceVersion}.0.0`);
  if (/[^\x00-\x7F]/.test(setup)) errors.push('setup/Setup.cs has a non-ASCII character');
  if (!read('tools/build.cmd').includes('setup\\Setup.cs')) errors.push('tools/build.cmd must compile setup/Setup.cs');
}

// 2. build list
const build = read('tools/build.cmd');
const listed = [...build.matchAll(/src\\([A-Za-z]+\.cs)/g)].map((m) => m[1]);
const present = fs.readdirSync(path.join(root, 'src')).filter((f) => f.endsWith('.cs'));
for (const f of present) if (!listed.includes(f)) errors.push(`src/${f} is not compiled by tools/build.cmd`);
for (const f of listed) if (!present.includes(f)) errors.push(`tools/build.cmd compiles src/${f}, which does not exist`);

// voice packs: well-formed enough for the loader (src/VoicePacks.cs), ASCII, unique keys, a version
const packDir = path.join(root, 'voicepacks');
if (fs.existsSync(packDir)) {
  for (const f of fs.readdirSync(packDir).filter((n) => n.endsWith('.xml'))) {
    const text = read(path.join('voicepacks', f));
    const where = 'voicepacks/' + f;
    if (/[^\x00-\x7F]/.test(text)) errors.push(`${where} has a non-ASCII character`);
    const head = text.match(/<voicepack\b[^>]*>/);
    if (!head) { errors.push(`${where}: no <voicepack> element`); continue; }
    const ver = (head[0].match(/\bversion="(\d+)"/) || [])[1];
    if (!ver || parseInt(ver, 10) < 1) errors.push(`${where}: version must be a whole number from 1`);
    const seen = new Set();
    for (const m of text.matchAll(/<message\b([^>]*)>([\s\S]*?)<\/message>/g)) {
      const key = (m[1].match(/\bkey="([^"]+)"/) || [])[1];
      if (!key) { errors.push(`${where}: a message has no key`); continue; }
      if (seen.has(key)) errors.push(`${where}: key ${key} appears twice`);
      seen.add(key);
      const actor = (m[1].match(/\bactor="(\d+)"/) || [])[1];
      if (actor && (parseInt(actor, 10) < 1 || parseInt(actor, 10) > 11)) errors.push(`${where}: ${key} actor ${actor} is not 1-11`);
      const sfx = /\bsfx="1"/.test(m[1]);
      if (!sfx && !/<o(?:\s[^>]*)?>[^<]+<\/o>/.test(m[2])) errors.push(`${where}: ${key} has no <o> text`);
      // six options for each message (for the technicians six for each sex); a futile message has two silent options first and the default third
      if (!sfx) {
        const opts = [...m[2].matchAll(/<o\b([^>]*?)(?:\/>|>([^<]*)<\/o>)/g)].map((x) => ({ sex: (x[1].match(/\bsex="([mf])"/) || [])[1] || '', silent: /\bsilent="1"/.test(x[1]) }));
        const sexed = opts.some((o) => o.sex);
        const count = (s) => opts.filter((o) => !o.sex || o.sex === s).length;
        if (sexed ? (count('m') !== 6 || count('f') !== 6) : opts.length !== 6) errors.push(`${where}: ${key} must have six options`);
        if (/\bfutile="1"/.test(m[1]) && !(opts.length > 2 && opts[0].silent && opts[1].silent && !opts[2].silent)) errors.push(`${where}: ${key} is futile: options 1 and 2 silent, 3 the default`);
        if (!/\bfutile="1"/.test(m[1]) && opts.some((o) => o.silent)) errors.push(`${where}: ${key} has a silent option but is not futile`);
      }
      for (const o of m[2].matchAll(/<o\b([^>]*)>/g)) { const sx = (o[1].match(/\bsex="([^"]*)"/) || [])[1]; if (sx !== undefined && sx !== 'm' && sx !== 'f') errors.push(`${where}: ${key} has an option with sex="${sx}" (m or f)`); }
    }
    if (seen.size === 0) errors.push(`${where}: no message`);
  }
}
if (!fs.existsSync(path.join(root, 'docs/DEVELOPER_GUIDE.md'))) errors.push('docs/DEVELOPER_GUIDE.md is missing');
else if (!read('tools/build.cmd').includes('DEVELOPER_GUIDE.md')) errors.push('tools/build.cmd must compile docs/DEVELOPER_GUIDE.md into the program');

// licence file present
if (!fs.existsSync(path.join(root, 'LICENSE'))) errors.push('LICENSE is missing');

// 3. ASCII
for (const f of present) {
  const text = read(path.join('src', f));
  const m = text.match(/[^\x00-\x7F]/);
  if (m) errors.push(`src/${f} has a non-ASCII character (${JSON.stringify(m[0])})`);
}

// 4. tracked files
try {
  const tracked = cp.execFileSync('git', ['-C', root, 'ls-files'], { encoding: 'utf8' }).split(/\r?\n/).filter(Boolean);
  const bad = tracked.filter((f) => /\.(exe|dll|wav|onnx|zip|pdb)$/i.test(f) || /^(piper|voices|backups|audio|bin|old|voicepacks\/obsolete)\//i.test(f) || /^HPGMissionCompanion\.json$/i.test(f));
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
