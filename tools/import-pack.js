#!/usr/bin/env node

'use strict';

// One-shot importer: builds a voice pack XML (see docs/architecture/VOICEPACKS.md) from the voice messages spreadsheet of the HEMS mission.
//   node tools/import-pack.js --xlsx AND_voice_messages.xlsx --out voicepacks/EN.xml --version 1 [--name EN] [--language EN]
//        [--mission-src <mission-src dir>] [--sound <dir with the wav files>] [--extras tools/pack-extras-EN.json,tools/pack-dialogs-EN.json] [--report <file>]
// The spreadsheet columns: LVAR, file name, text of the message (the wav), role, trigger, proposed correction, 3 numbered alternatives.
// Options of a message: the text of the wav (when it is close to the correction), the correction, the three alternatives (duplicates removed).
// With --mission-src the call sites of each LVAR give the seconds the mission waits after the message; with --sound the wav lengths are read.
// Where the mission waits less than the wav lasts, only options of a similar duration are kept ("tight" messages).
// The XML is the source after this import: edit it by hand from then on, and raise the version at every change.
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

function arg(name, dflt) {
  const i = process.argv.indexOf('--' + name);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : dflt;
}
const xlsxPath = arg('xlsx');
const outPath = arg('out', 'voicepacks/EN.xml');
const version = parseInt(arg('version', '1'), 10);
const packName = arg('name', 'EN');
const language = arg('language', 'EN');
const missionSrc = arg('mission-src');
const soundDir = arg('sound');
const reportPath = arg('report');
const excluded = (arg('exclude', '') || '').split(',').filter(Boolean);       // keys left out of the pack (for example SYS.NEWMSG: the tablet plays its own tone)
const extrasPaths = (arg('extras', '') || '').split(',').filter(Boolean);
if (!xlsxPath) { console.error('usage: node tools/import-pack.js --xlsx <file> [--out voicepacks/EN.xml] [--version 1] [--mission-src dir] [--sound dir]'); process.exit(2); }

// ---- minimal zip reader (the xlsx is a zip file) ----------------------------------------------------
function readZip(file) {
  const buf = fs.readFileSync(file);
  let eocd = -1;
  for (let i = buf.length - 22; i >= 0; i--) if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) throw new Error('not a zip file');
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  const files = {};
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error('bad central directory');
    const method = buf.readUInt16LE(p + 10);
    const csize = buf.readUInt32LE(p + 20);
    const nlen = buf.readUInt16LE(p + 28), elen = buf.readUInt16LE(p + 30), clen = buf.readUInt16LE(p + 32);
    const lho = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nlen);
    const ln = buf.readUInt16LE(lho + 26), le = buf.readUInt16LE(lho + 28);
    const data = buf.slice(lho + 30 + ln + le, lho + 30 + ln + le + csize);
    files[name] = method === 0 ? data : zlib.inflateRawSync(data);
    p += 46 + nlen + elen + clen;
  }
  return files;
}

const unesc = (s) => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');
function readSheet(file) {
  const z = readZip(file);
  const ss = z['xl/sharedStrings.xml'].toString('utf8');
  const strs = [...ss.matchAll(/<si>([\s\S]*?)<\/si>/g)].map((m) => unesc([...m[1].matchAll(/<t[^>]*>([\s\S]*?)<\/t>/g)].map((x) => x[1]).join('')));
  const sh = z['xl/worksheets/sheet1.xml'].toString('utf8');
  const rows = [];
  for (const r of sh.matchAll(/<row [^>]*r="(\d+)"[^>]*>([\s\S]*?)<\/row>/g)) {
    const o = {};
    for (const c of r[2].matchAll(/<c r="([A-Z]+)\d+"([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)) {
      const t = (c[2].match(/t="(\w+)"/) || [])[1];
      const v = (c[3] || '').match(/<v>([\s\S]*?)<\/v>/);
      let val = v ? v[1] : '';
      if (t === 's') val = strs[+val];
      else if (t === 'inlineStr') val = unesc(((c[3] || '').match(/<t[^>]*>([\s\S]*?)<\/t>/) || [])[1] || '');
      o[c[1]] = val;
    }
    rows.push(o);
  }
  return rows;
}

// ---- helpers -----------------------------------------------------------------------------------------
const ascii = (s) => (s || '').replace(/[\u2018\u2019\u201B]/g, "'").replace(/[\u201C\u201D]/g, '"').replace(/[\u2013\u2014]/g, '-').replace(/\u2026/g, '...').replace(/\u00A0/g, ' ').replace(/[^\x00-\x7F]/g, '').replace(/\s+/g, ' ').trim();
const norm = (s) => s.toLowerCase().replace(/[^a-z0-9 ]/g, ' ').replace(/\s+/g, ' ').trim();
const tokens = (s) => new Set(norm(s).split(' ').filter(Boolean));
function jaccard(a, b) {
  const x = tokens(a), y = tokens(b);
  let i = 0;
  for (const t of x) if (y.has(t)) i++;
  const u = x.size + y.size - i;
  return u === 0 ? 1 : i / u;
}
function alternatives(g) {
  if (!g) return [];
  return g.split(/\r?\n/).map((l) => ascii(l.replace(/^\s*\d\)\s*/, ''))).filter(Boolean);
}
const xmlEsc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

// LVAR of the spreadsheet -> key, actor
function keyOf(lvar) {
  let m;
  const v = lvar.replace(/^L:/, '');
  if ((m = v.match(/^(CPL|PLT|PAX2|PAX3|FRA|MAR)AND(\d+)$/))) return { key: m[1] + '.' + m[2], role: m[1] };
  if ((m = v.match(/^SARAAND(\d+)$/))) return { key: 'SARA.' + m[1], role: 'SARA' };
  if ((m = v.match(/^ANDHW(\d+)$/))) return { key: 'HW.' + m[1], role: 'HW', n: +m[1] };
  if ((m = v.match(/^AND(\d+)$/))) return { key: 'PAX1.' + m[1], role: 'PAX1' };
  if (v === 'ANDDISPATCH2') return { key: 'SYS.DISPATCH2', role: 'SYS' };
  if ((m = v.match(/^ANDDISPATCHED(\d*)$/))) return { key: 'SYS.DISPATCHED' + m[1], role: 'SYS' };
  if (v === 'ANDNEWMSG') return { key: 'SYS.NEWMSG', role: 'SYS' };
  if (v === 'DEADAND') return { key: 'SYS.DEAD', role: 'SYS' };
  if (v === 'ANDTEST1') return { key: 'SYS.TEST1', role: 'SYS' };
  if (v === 'ANDFOOL') return { key: 'SYS.FOOL', role: 'SYS' };
  return { key: v.replace(/[^A-Za-z0-9_.-]/g, '_'), role: 'SYS' };
}
const ACTOR = { PLT: 1, CPL: 2, PAX3: 3, PAX1: 4, PAX2: 5, FRA: 6, SARA: 7, MAR: 9, HW: 1, SYS: 1 };

// ---- read the spreadsheet ----------------------------------------------------------------------------
const sheet = readSheet(xlsxPath).slice(1).filter((r) => r.A);
console.log('rows:', sheet.length);

// wav lengths
function wavSeconds(file) {
  try {
    const b = fs.readFileSync(file);
    let p = 12, rate = 0, dataLen = 0;
    while (p + 8 <= b.length) {
      const id = b.toString('ascii', p, p + 4), len = b.readUInt32LE(p + 4);
      if (id === 'fmt ') rate = b.readUInt32LE(p + 16);   // bytes per second
      if (id === 'data') { dataLen = len; break; }
      p += 8 + len + (len & 1);
    }
    return rate ? dataLen / rate : -1;
  } catch (e) { return -1; }
}

// call sites of the mission: normalised LVAR -> seconds waited after it (minimum over the sites), and the number of sites
const sites = {};
function walk(d) { return fs.readdirSync(d, { withFileTypes: true }).flatMap((e) => e.isDirectory() ? walk(path.join(d, e.name)) : e.name.endsWith('.json') ? [path.join(d, e.name)] : []); }
if (missionSrc) {
  for (const f of walk(missionSrc)) {
    const lines = fs.readFileSync(f, 'utf8').split(/\r?\n/);
    lines.forEach((l, i) => {
      const found = [];
      for (const m of l.matchAll(/"set":\s*\{\s*"var":\s*\["L:([A-Z0-9_]*?)\{local:VCP\}([A-Z0-9_]*)"/g)) found.push('L:' + m[1] + 'AND' + m[2]);
      for (const m of l.matchAll(/"set":\s*\{\s*"var":\s*\["L:\{local:VCP\}([A-Z0-9_]*)"/g)) found.push('L:AND' + m[1]);
      for (const k of found) {
        let sleep = -1;
        for (let j = i + 1; j < Math.min(lines.length, i + 6); j++) {
          const t = lines[j].trim();
          if (/^[\]\}\),]+$/.test(t) || t === '') continue;
          const s = t.match(/^\{\s*"sleep":\s*([0-9.]+)/) || t.match(/^\{\s*"sleep":\s*\{\s*"rand":\s*\[\s*([0-9.]+)/);
          sleep = s ? parseFloat(s[1]) : 0;
          break;
        }
        const e = sites[k] || (sites[k] = { n: 0, sleep: -1 });
        e.n++;
        if (sleep >= 0 && (e.sleep < 0 || sleep < e.sleep)) e.sleep = sleep;
      }
    });
  }
}

// ---- group the rows by LVAR ---------------------------------------------------------------------------
const byLvar = new Map();
for (const r of sheet) {
  if (!byLvar.has(r.A)) byLvar.set(r.A, []);
  byLvar.get(r.A).push(r);
}

// speaking rate (characters per second) of the existing wavs, to estimate the length of a text
const rates = [];
if (soundDir) {
  for (const r of sheet) {
    const text = ascii(r.F || r.C || '');
    if (!text || text[0] === '[') continue;
    const d = wavSeconds(path.join(soundDir, r.B));
    if (d > 0.8) rates.push(text.length / d);
  }
}
rates.sort((a, b) => a - b);
const rate = rates.length ? rates[Math.floor(rates.length / 2)] : 14;
console.log('speaking rate (chars/s):', rate.toFixed(1), 'from', rates.length, 'wavs');

const report = [];
const messages = [];
let tightCount = 0, droppedByDuration = 0, droppedGarbled = 0;
for (const [lvar, rows] of byLvar) {
  const k = keyOf(lvar);
  const first = rows[0];
  const sfx = rows.every((r) => /^\s*\[/.test(r.C || ''));
  const msg = { key: k.key, lvar, role: k.role, actor: ACTOR[k.role] || 1, wav: first.B, sfx, trigger: ascii(first.E || ''), options: [], sleep: -1, wavSeconds: -1, tight: false, draft: false };
  if (k.role === 'HW' && (k.n === 5 || k.n === 6)) msg.actor = 2;
  if (first.D && /Marshal/i.test(first.D)) msg.actor = 9;
  const site = sites[lvar];
  if (site) msg.sleep = site.sleep;
  if (soundDir) msg.wavSeconds = wavSeconds(path.join(soundDir, first.B));
  if (sfx) { messages.push(msg); continue; }

  // candidate options of every row, each with the length of its own wav
  const perRow = rows.map((r) => {
    const c = ascii(r.C || ''), f = ascii(r.F || '');
    const list = [];
    if (f) {
      const j = jaccard(c, f);
      if (j >= 0.9) list.push(f);
      else if (j >= 0.55) { list.push(c); list.push(f); }
      else { list.push(f); droppedGarbled++; }
    } else if (c) list.push(c);
    for (const a of alternatives(r.G)) list.push(a);
    return { list, d: soundDir ? wavSeconds(path.join(soundDir, r.B)) : -1 };
  });
  // order: the first option of every row, then the rest of every row in turn; at most 5 (8 when the LVAR has several wav files)
  const cap = rows.length > 1 ? 8 : 5;
  const seen = new Set();
  const picked = [];
  const maxLen = Math.max(...perRow.map((p) => p.list.length));
  for (let i = 0; i < maxLen && picked.length < cap; i++) {
    for (const p of perRow) {
      if (i >= p.list.length || picked.length >= cap) continue;
      const t = p.list[i], n = norm(t);
      if (!n || seen.has(n)) continue;
      seen.add(n);
      picked.push({ text: t, d: p.d });
    }
  }
  // tight message: the mission waits a short, explicit time after it (less than the wav lasts plus half a second): keep options of a similar duration.
  // Where no sleep follows the message (sleep 0) nothing waits for it, so the length of the options does not matter.
  const wd = msg.wavSeconds;
  msg.tight = msg.sleep > 0 && wd > 0 && msg.sleep < wd + 0.5;
  if (msg.tight) {
    tightCount++;
    const kept = picked.filter((o, idx) => {
      if (idx === 0) return true;
      const ref = o.d > 0 ? o.d : wd;
      const limit = Math.max(ref, msg.sleep) * 1.15 + 0.4;
      const est = o.text.length / rate;
      const ok = est <= limit && est >= ref * 0.5;
      if (!ok) droppedByDuration++;
      return ok;
    });
    picked.length = 0; picked.push(...kept);
  }
  msg.options = picked.map((o) => o.text);
  if (rows[0].D) msg.role = rows[0].D;
  messages.push(msg);
}

// messages that have no row in the spreadsheet (texts written from the wav file names: draft)
for (const extrasPath of extrasPaths) {
  const dialogs = /dialogs/i.test(extrasPath);       // dialogs file: new messages with a group, not drafts of existing wavs
  const extras = JSON.parse(fs.readFileSync(extrasPath, 'utf8')).messages;
  for (const x of extras) {
    const k = x.lvar ? keyOf(x.lvar.replace('{VCP}', 'AND')) : { key: x.key, role: x.key.split('.')[0] };
    if (messages.some((m) => m.key === x.key)) { console.log('extras: ' + x.key + ' already in the pack, skipped'); continue; }
    const site = x.lvar ? sites[x.lvar.replace('{VCP}', 'AND')] : null;
    messages.push({ key: x.key, lvar: x.lvar, role: k.role, actor: x.actor || ACTOR[k.role] || 1, wav: x.wav || '', sfx: false, trigger: ascii(x.trigger || ''), options: x.o.map(ascii), sleep: site ? site.sleep : -1, tight: false, draft: !dialogs, group: x.group || '', seq: x.seq || 0, isNew: dialogs, radio: x.radio || 'auto' });
  }
}

// ---- write the XML ------------------------------------------------------------------------------------
const L = [];
L.push('<?xml version="1.0" encoding="utf-8"?>');
L.push('<!-- HPG Mission Companion voice pack ' + packName + '. Edit the texts freely; raise version="" at every change (see docs/architecture/VOICEPACKS.md). -->');
L.push('<voicepack name="' + xmlEsc(packName) + '" language="' + xmlEsc(language) + '" version="' + version + '" vcp="APP" title="English crew voices of the HEMS missions">');
L.push('  <names>');
L.push('    <name role="PLT">Captain</name>');
L.push('    <name role="CPL">Copilot</name>');
L.push('    <name role="PAX1">Chief</name>');
L.push('    <name role="PAX2">Paramedic</name>');
L.push('    <name role="PAX3">Doc</name>');
L.push('    <name role="VEH_A">ambulance</name>');
L.push('    <name role="VEH_F">fire engine</name>');
L.push('    <name role="VEH_P">police</name>');
L.push('    <name role="AND">and</name>');
L.push('    <name role="IS">is</name>');
L.push('    <name role="ARE">are</name>');
L.push('    <pool role="PLT">Mark, John, Paul, Peter, Chris, David, Simon</pool>');
L.push('    <pool role="CPL">Anna, Laura, Emma, Kate, Julia, Nina, Clara</pool>');
L.push('    <pool role="PAX1">Tom, Ben, Jack, Luke, Sam, Ryan, Ian</pool>');
L.push('    <pool role="PAX2">Eva, Lisa, Mia, Zoe, Ruth, Beth, Joan</pool>');
L.push('    <pool role="PAX3">Sue, Alex, Dan, Jane, Rob, Matt, Vera</pool>');
L.push('  </names>');
for (let i = messages.length - 1; i >= 0; i--) if (excluded.includes(messages[i].key)) { console.log('excluded ' + messages[i].key); messages.splice(i, 1); }
messages.sort((a, b) => a.key.localeCompare(b.key, 'en', { numeric: true }));
for (const m of messages) {
  const tpl = !m.lvar ? '' : (m.lvar.includes('{VCP}') ? m.lvar : m.lvar.replace('AND', '{VCP}'));      // L:CPLAND5 -> L:CPL{VCP}5: the mission puts its voice code in {VCP}
  let a = '    <message key="' + xmlEsc(m.key) + '" actor="' + m.actor + '" radio="' + (m.radio || 'auto') + '"' + (m.group ? ' group="' + xmlEsc(m.group) + '" seq="' + m.seq + '"' : '') + (tpl ? ' lvar="' + xmlEsc(tpl) + '"' : '') + (m.wav ? ' wav="' + xmlEsc(m.wav) + '"' : '');
  if (m.sleep >= 0) a += ' sleep="' + m.sleep + '"';
  if (m.tight) a += ' tight="1"';
  if (m.sfx) a += ' sfx="1"';
  if (m.draft) a += ' draft="1"';
  if (m.isNew) a += ' new="1"';
  if (m.trigger) a += ' trigger="' + xmlEsc(m.trigger) + '"';
  L.push(a + '>');
  for (const o of m.options) L.push('      <o>' + xmlEsc(o) + '</o>');
  L.push('    </message>');
}
L.push('</voicepack>');
fs.mkdirSync(path.dirname(path.resolve(outPath)), { recursive: true });
fs.writeFileSync(outPath, L.join('\n') + '\n', 'utf8');

// ---- report -------------------------------------------------------------------------------------------
const verbal = messages.filter((m) => !m.sfx);
const dist = {};
for (const m of verbal) dist[m.options.length] = (dist[m.options.length] || 0) + 1;
report.push('messages ' + messages.length + ' (' + verbal.length + ' with text, ' + (messages.length - verbal.length) + ' sound only)');
report.push('options per message: ' + JSON.stringify(dist));
report.push('tight messages (the mission waits less than the wav lasts): ' + tightCount + ', options dropped for duration: ' + droppedByDuration + ', garbled wav texts replaced by the correction: ' + droppedGarbled);
const missing = Object.keys(sites).filter((k) => !byLvar.has(k));
report.push('LVARs used by the mission with no row in the spreadsheet: ' + (missing.join(', ') || 'none'));
const unused = messages.filter((m) => missionSrc && !sites[m.lvar]).map((m) => m.key);
report.push('messages with no call site in the mission yet (' + unused.length + '): ' + unused.join(', '));
console.log(report.join('\n'));
if (reportPath) fs.writeFileSync(reportPath, report.join('\n') + '\n', 'utf8');
console.log('written', outPath);
