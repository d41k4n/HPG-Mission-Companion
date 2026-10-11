#!/usr/bin/env node

'use strict';

// Applies the options of tools/variants-EN/*.json to a voice pack: six options for every message.
//   node tools/build-variants.js [--in voicepacks/EN.xml] [--out voicepacks/EN.xml] [--version 6] [--report <file>]
// File format: { "KEY": { "o": [6 texts], "futile": true } } or, for the technicians, { "KEY": { "m": [..], "f": [..], "futile": true } }.
//   - option 1 is the default (the wav text, corrected), 2 and 3 are professional, 4 to 6 more and more colloquial;
//   - a futile message (small talk nobody needs) has two silent options first (an empty text), the default is option 3, and its conversation is always coherent;
//   - for a technician message m and f are the options for the male and the female technician (six each; a futile message has two silent options shared by both and four for each sex);
//   - a key that is not in the pack is added as a new message (needs actor, group, seq, trigger like the other new messages).
// The XML is the source of the pack: this script only rewrites the options of the messages it knows. It stops when a message of the pack has no six options.
const fs = require('fs');
const path = require('path');

function arg(name, dflt) {
  const i = process.argv.indexOf('--' + name);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : dflt;
}
const root = path.resolve(__dirname, '..');
const inPath = arg('in', path.join(root, 'voicepacks', 'EN.xml'));
const outPath = arg('out', inPath);
const version = arg('version');
const reportPath = arg('report');
const xmlEsc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

const variants = {};
const dir = path.join(root, 'tools', 'variants-EN');
for (const f of fs.readdirSync(dir).filter((n) => n.endsWith('.json')).sort()) {
  const j = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));
  for (const [k, v] of Object.entries(j)) {
    if (k.startsWith('_')) continue;
    if (variants[k]) throw new Error('key ' + k + ' is in two files');
    variants[k] = v;
  }
}

function optionLines(v, key) {
  const out = [];
  const bad = [];
  if (v.o) {
    if (v.o.length !== 6) bad.push(key + ': ' + v.o.length + ' options');
    if (v.futile && (v.o[0] !== '' || v.o[1] !== '' || !v.o[2])) bad.push(key + ': a futile message needs two silent options and the default as third');
    v.o.forEach((t) => out.push(t === '' ? '      <o silent="1"/>' : '      <o>' + xmlEsc(t) + '</o>'));
  } else {
    const lead = v.futile ? 2 : 0;
    const per = 6 - lead;
    if (!v.m || !v.f || v.m.length !== per || v.f.length !== per) bad.push(key + ': a technician message needs ' + per + ' options for each sex');
    for (let i = 0; i < lead; i++) out.push('      <o silent="1"/>');
    (v.m || []).forEach((t) => out.push('      <o sex="m">' + xmlEsc(t) + '</o>'));
    (v.f || []).forEach((t) => out.push('      <o sex="f">' + xmlEsc(t) + '</o>'));
  }
  return { out, bad };
}

let text = fs.readFileSync(inPath, 'utf8').replace(/\r\n/g, '\n');
const problems = [];
const seen = new Set();
let changed = 0;
text = text.replace(/<message\b([^>]*)>([\s\S]*?)<\/message>/g, (all, attrs, inner) => {
  const key = (attrs.match(/\bkey="([^"]+)"/) || [])[1];
  if (/\bsfx="1"/.test(attrs) || /\btext="1"/.test(attrs)) return all;       // sounds, and the generated texts of the mission (tools/sync-pack-texts.js)
  const v = variants[key];
  if (!v) { problems.push(key + ': no variants'); return all; }
  seen.add(key);
  const r = optionLines(v, key);
  problems.push(...r.bad);
  if (/\btight="1"/.test(attrs)) {      // the mission waits less than the wav lasts: an option may be a little longer than the default, not much (18 characters a second)
    const sl = parseFloat((attrs.match(/\bsleep="([0-9.]+)"/) || [])[1] || '0');
    const texts = v.o ? v.o : [...(v.m || []), ...(v.f || [])];
    const dflt = (v.o ? v.o.find((x) => x) : (v.m || [''])[0]) || '';
    const limit = (Math.max(sl, dflt.length / 18.1) * 1.15 + 0.4) * 18.1;
    texts.forEach((x, i) => { if (x.length > limit) problems.push(key + ': option ' + (i + 1) + ' is ' + x.length + ' characters, the limit of this tight message is ' + Math.floor(limit)); });
  }
  let a = attrs.replace(/\s+futile="1"/, '').replace(/\s+draft="1"/, '').replace(/\s+tight="1"/, (m) => m);
  if (v.futile) a += ' futile="1"';
  changed++;
  return '<message' + a + '>\n' + r.out.join('\n') + '\n    </message>';
});
// new messages
const added = [];
for (const [key, v] of Object.entries(variants)) {
  if (seen.has(key)) continue;
  if (!v.actor) { problems.push(key + ': not in the pack and no actor'); continue; }
  const r = optionLines(v, key);
  problems.push(...r.bad);
  let a = ' key="' + key + '" actor="' + v.actor + '" radio="' + (v.radio || 'auto') + '"' + (v.group ? ' group="' + v.group + '" seq="' + (v.seq || 0) + '"' : '') + (v.futile ? ' futile="1"' : '') + ' new="1"' + (v.trigger ? ' trigger="' + xmlEsc(v.trigger) + '"' : '');
  added.push('    <message' + a + '>\n' + r.out.join('\n') + '\n    </message>');
}
if (added.length) text = text.replace('</voicepack>', added.join('\n') + '\n</voicepack>');
if (version) text = text.replace(/(<voicepack\b[^>]*\bversion=")\d+(")/, '$1' + version + '$2');
if (problems.length) {
  console.error('build-variants FAIL (' + problems.length + '):\n - ' + problems.slice(0, 60).join('\n - '));
  process.exit(1);
}
fs.writeFileSync(outPath, text, 'utf8');
const msg = 'rewrote ' + changed + ' messages, added ' + added.length + ', version ' + (version || 'unchanged') + ' -> ' + outPath;
console.log(msg);
if (reportPath) fs.writeFileSync(reportPath, msg + '\n');
