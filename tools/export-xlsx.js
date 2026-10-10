#!/usr/bin/env node

'use strict';

// Writes the voice messages spreadsheet from a voice pack (the pack is the source since pack EN 6).
//   node tools/export-xlsx.js --pack voicepacks/EN.xml --old AND_voice_messages.xlsx --out AND_voice_messages_new.xlsx
// --old is the spreadsheet the pack was made from (columns A LVAR, B file name, C text of the wav, D role, E trigger): its rows keep those five columns.
// Sheet "Voice messages": the original layout, with the corrected default (F) and the other options (G) taken from the pack; messages that the pack added come after.
// Sheet "Options": every message of the pack with its six options in separate columns (sex m and f for the technicians), futile flag, actor, group, status.
// Sheet "Notes": the conventions. Node built-ins only.
const fs = require('fs');
const zlib = require('zlib');

function arg(name, dflt) {
  const i = process.argv.indexOf('--' + name);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : dflt;
}
const packPath = arg('pack', 'voicepacks/EN.xml');
const oldPath = arg('old');
const outPath = arg('out');
if (!outPath) { console.error('usage: node tools/export-xlsx.js --pack voicepacks/EN.xml --old old.xlsx --out new.xlsx'); process.exit(2); }

// ---- zip reader / writer ------------------------------------------------------------------------------
function readZip(file) {
  const buf = fs.readFileSync(file);
  let eocd = -1;
  for (let i = buf.length - 22; i >= 0; i--) if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) throw new Error('not a zip file');
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  const files = {};
  for (let n = 0; n < count; n++) {
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
const crcTable = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
function crc32(buf) { let c = 0xFFFFFFFF; for (let i = 0; i < buf.length; i++) c = crcTable[(c ^ buf[i]) & 0xFF] ^ (c >>> 8); return (c ^ 0xFFFFFFFF) >>> 0; }
function writeZip(entries) {
  const parts = [], central = [];
  let offset = 0;
  for (const [name, data] of entries) {
    const nameBuf = Buffer.from(name, 'utf8');
    const comp = zlib.deflateRawSync(data);
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(20, 4); local.writeUInt16LE(0x0800, 6); local.writeUInt16LE(8, 8);
    local.writeUInt16LE(0, 10); local.writeUInt16LE(0x21, 12); local.writeUInt32LE(crc, 14); local.writeUInt32LE(comp.length, 18); local.writeUInt32LE(data.length, 22); local.writeUInt16LE(nameBuf.length, 26);
    parts.push(local, nameBuf, comp);
    const cen = Buffer.alloc(46);
    cen.writeUInt32LE(0x02014b50, 0); cen.writeUInt16LE(20, 4); cen.writeUInt16LE(20, 6); cen.writeUInt16LE(0x0800, 8); cen.writeUInt16LE(8, 10);
    cen.writeUInt16LE(0, 12); cen.writeUInt16LE(0x21, 14); cen.writeUInt32LE(crc, 16); cen.writeUInt32LE(comp.length, 20); cen.writeUInt32LE(data.length, 24); cen.writeUInt16LE(nameBuf.length, 28);
    cen.writeUInt32LE(offset, 42);
    central.push(cen, nameBuf);
    offset += local.length + nameBuf.length + comp.length;
  }
  const cenBuf = Buffer.concat(central);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10); end.writeUInt32LE(cenBuf.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...parts, cenBuf, end]);
}

// ---- the old spreadsheet ------------------------------------------------------------------------------
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
function keyOf(lvar) {
  let m;
  const v = lvar.replace(/^L:/, '');
  if ((m = v.match(/^(CPL|PLT|PAX2|PAX3|FRA|MAR)AND(\d+)$/))) return m[1] + '.' + m[2];
  if ((m = v.match(/^SARAAND(\d+)$/))) return 'SARA.' + m[1];
  if ((m = v.match(/^ANDHW(\d+)$/))) return 'HW.' + m[1];
  if ((m = v.match(/^AND(\d+)$/))) return 'PAX1.' + m[1];
  if (v === 'ANDDISPATCH2') return 'SYS.DISPATCH2';
  if ((m = v.match(/^ANDDISPATCHED(\d*)$/))) return 'SYS.DISPATCHED' + m[1];
  if (v === 'ANDNEWMSG') return 'SYS.NEWMSG';
  if (v === 'DEADAND') return 'SYS.DEAD';
  if (v === 'ANDTEST1') return 'SYS.TEST1';
  if (v === 'ANDFOOL') return 'SYS.FOOL';
  return v.replace(/[^A-Za-z0-9_.-]/g, '_');
}

// ---- the pack -----------------------------------------------------------------------------------------
const xml = fs.readFileSync(packPath, 'utf8').replace(/\r\n/g, '\n');
const version = (xml.match(/<voicepack\b[^>]*\bversion="(\d+)"/) || [])[1];
const messages = [];
for (const m of xml.matchAll(/<message\b([^>]*)>([\s\S]*?)<\/message>/g)) {
  const g = (n) => unesc((m[1].match(new RegExp('\\b' + n + '="([^"]*)"')) || [])[1] || '');
  const opts = [...m[2].matchAll(/<o\b([^>]*?)(?:\/>|>([^<]*)<\/o>)/g)].map((x) => ({ sex: (x[1].match(/\bsex="([mf])"/) || [])[1] || '', silent: /\bsilent="1"/.test(x[1]), text: unesc((x[2] || '').trim()) }));
  messages.push({ key: g('key'), actor: g('actor'), group: g('group'), seq: g('seq'), lvar: g('lvar'), wav: g('wav'), trigger: g('trigger'), sleep: g('sleep'), tight: g('tight') === '1', sfx: g('sfx') === '1', futile: g('futile') === '1', isNew: g('new') === '1', opts });
}
const byKey = new Map(messages.map((m) => [m.key, m]));
const actorName = { 1: 'Pilot', 2: 'Copilot', 3: 'Doctor', 4: 'HEMS1 (chief)', 5: 'HEMS2', 6: 'Technician male', 7: 'Technician female', 8: 'Dispatcher', 9: 'Ground unit', 10: 'Patient / extra', 11: 'Checklist' };

// options of a message as {m: [...], f: [...]} (six each) or {all: [...]}
function split(m) {
  if (m.opts.some((o) => o.sex)) {
    const pick = (s) => m.opts.filter((o) => !o.sex || o.sex === s);
    return { m: pick('m'), f: pick('f') };
  }
  return { all: m.opts };
}
const label = (o, i) => (o.silent ? '(silent)' : o.text);
function numbered(list, from) { return list.map((o, i) => (i + 1) + ') ' + label(o)).slice(from).join('\n'); }
function defaultAndOthers(m) {
  const s = split(m);
  const defIdx = m.futile ? 2 : 0;
  if (s.all) return { def: s.all[defIdx] ? label(s.all[defIdx]) : '', others: numbered(s.all, defIdx + 1).split('\n').filter(Boolean).join('\n') };
  const d = (list) => (list[defIdx] ? label(list[defIdx]) : '');
  return { def: '[m] ' + d(s.m) + '\n[f] ' + d(s.f), others: '[m]\n' + numbered(s.m, defIdx + 1) + '\n[f]\n' + numbered(s.f, defIdx + 1) };
}

// ---- sheet builders -----------------------------------------------------------------------------------
const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, '');
const colName = (i) => { let s = ''; i++; while (i > 0) { const r = (i - 1) % 26; s = String.fromCharCode(65 + r) + s; i = Math.floor((i - 1) / 26); } return s; };
function sheetXml(rows, widths, headerRows) {
  const L = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>', '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'];
  L.push('<sheetViews><sheetView workbookViewId="0"><pane ySplit="' + headerRows + '" topLeftCell="A' + (headerRows + 1) + '" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>');
  L.push('<sheetFormatPr defaultRowHeight="14.25"/>');
  L.push('<cols>' + widths.map((w, i) => '<col min="' + (i + 1) + '" max="' + (i + 1) + '" width="' + w + '" customWidth="1"/>').join('') + '</cols>');
  L.push('<sheetData>');
  rows.forEach((row, r) => {
    const head = r < headerRows;
    L.push('<row r="' + (r + 1) + '">' + row.map((v, c) => v === '' || v === undefined || v === null ? '' : '<c r="' + colName(c) + (r + 1) + '" s="' + (head ? 1 : 2) + '" t="inlineStr"><is><t xml:space="preserve">' + esc(v) + '</t></is></c>').join('') + '</row>');
  });
  L.push('</sheetData>');
  L.push('</worksheet>');
  return L.join('');
}

// sheet 1: the original layout
const old = oldPath ? readSheet(oldPath).filter((r) => r.A) : [];
const rows1 = [['LVAR', 'nome file', 'testo del messaggio', 'ruolo', 'trigger (azione / tablet)', 'correzione proposta (opzione di default)', 'proposte alternative (numero = opzione del pack EN ' + version + ')', 'chiave pack', 'futile (chiacchiere)', 'stato']];
const used = new Set();
for (const r of old.slice(1)) {
  const key = keyOf(r.A);
  const m = byKey.get(key);
  if (!m) { rows1.push([r.A, r.B || '', r.C || '', r.D || '', r.E || '', r.F || '', r.G || '', key, '', 'non nel pack (escluso)']); continue; }
  used.add(key);
  if (m.sfx) { rows1.push([r.A, r.B || '', r.C || '', r.D || '', r.E || '', r.F || '', r.G || '', key, '', 'suono senza voce']); continue; }
  const d = defaultAndOthers(m);
  const was = (r.F || r.C || '').trim();
  const changed = d.def.replace(/^\[m\] /, '').split('\n')[0].trim() !== was;
  rows1.push([r.A, r.B || '', r.C || '', r.D || '', r.E || m.trigger || '', d.def, d.others, key, m.futile ? 'si: opzioni 1-2 vuote, 3 default' : '', changed ? 'corretto / riscritto' : 'invariato']);
}
for (const m of messages) {
  if (used.has(m.key) || m.sfx) continue;
  const d = defaultAndOthers(m);
  rows1.push([m.lvar || '(nessuna LVAR: tag say)', m.wav || '', '', m.key.split('.')[0], m.trigger || '', d.def, d.others, m.key, m.futile ? 'si: opzioni 1-2 vuote, 3 default' : '', 'aggiunto']);
}
// sheet 2: options in columns
const rows2 = [['chiave', 'attore', 'gruppo', 'seq', 'futile', 'sesso tecnico', 'trigger', 'sleep (s)', 'tight', 'opzione 1', 'opzione 2', 'opzione 3', 'opzione 4', 'opzione 5', 'opzione 6', 'stato']];
for (const m of messages) {
  if (m.sfx) continue;
  const s = split(m);
  const lists = s.all ? [['', s.all]] : [['m', s.m], ['f', s.f]];
  for (const [sx, list] of lists) {
    rows2.push([m.key, (actorName[m.actor] || m.actor), m.group, m.seq, m.futile ? 'si' : '', sx === 'm' ? 'maschio' : (sx === 'f' ? 'femmina' : ''), m.trigger, m.sleep, m.tight ? 'si' : '', ...list.map((o) => label(o)), m.isNew || !used.has(m.key) ? 'aggiunto' : 'pack']);
  }
}
const rows3 = [
  ['Pack EN ' + version + ' - convenzioni'],
  ['Opzione 1 = default: il testo del wav, corretto. Resta sempre quella di default.'],
  ['Opzioni 2 e 3 = varianti professionali; 4, 5 e 6 = varianti sempre piu colloquiali (dove la frase lo permette). Cosi un filtro futuro puo tagliare le opzioni alte.'],
  ['Messaggi futili (chiacchiere non necessarie): opzioni 1 e 2 vuote (silenzio), opzione 3 = default, 4-6 le altre. La conversazione resta coerente (stesso numero di opzione in tutte le righe del gruppo).'],
  ['Tutti i messaggi hanno 6 opzioni; per i tecnici sei per ciascun sesso (i messaggi futili hanno due opzioni vuote in comune).'],
  ['Richiami (numeri, ore, Vy, Vtoss, VNE, direzioni dell\'hoist): le sei opzioni sono quasi uguali di proposito.'],
  ['I morti e i feriti gravissimi non parlano: la missione pubblica L:HMC_CREW_MUTE (CREW_DEAD_n oppure CREW_LIFESCORE_n sotto 15) e il Companion tace le battute di quell\'attore.'],
  ['Le chiacchiere (BAN.*, CRZ.*) partono solo se nessun ferito grave, nessun morto, nessuna emergenza (macro voice calm della missione).'],
  ['Il pack e la sorgente: tools/variants-EN/*.json -> tools/build-variants.js -> voicepacks/EN.xml; questo file e generato da tools/export-xlsx.js.']
];
const sheet3 = (() => {
  const L = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>', '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cols><col min="1" max="1" width="160" customWidth="1"/></cols><sheetData>'];
  rows3.forEach((r, i) => L.push('<row r="' + (i + 1) + '"><c r="A' + (i + 1) + '" s="' + (i === 0 ? 1 : 2) + '" t="inlineStr"><is><t xml:space="preserve">' + esc(r[0]) + '</t></is></c></row>'));
  L.push('</sheetData></worksheet>');
  return L.join('');
})();

const styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFD9E1F2"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"><alignment vertical="top" wrapText="1"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>';
const entries = [
  ['[Content_Types].xml', Buffer.from('<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>')],
  ['_rels/.rels', Buffer.from('<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>')],
  ['xl/workbook.xml', Buffer.from('<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Voice messages" sheetId="1" r:id="rId1"/><sheet name="Options" sheetId="2" r:id="rId2"/><sheet name="Notes" sheetId="3" r:id="rId3"/></sheets></workbook>')],
  ['xl/_rels/workbook.xml.rels', Buffer.from('<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/><Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>')],
  ['xl/styles.xml', Buffer.from(styles)],
  ['xl/worksheets/sheet1.xml', Buffer.from(sheetXml(rows1, [24, 40, 60, 14, 50, 60, 90, 14, 22, 18], 1))],
  ['xl/worksheets/sheet2.xml', Buffer.from(sheetXml(rows2, [14, 18, 12, 5, 7, 10, 40, 8, 6, 50, 50, 50, 50, 50, 50, 10], 1))],
  ['xl/worksheets/sheet3.xml', Buffer.from(sheet3)]
];
fs.writeFileSync(outPath, writeZip(entries));
console.log('written ' + outPath + ': ' + (rows1.length - 1) + ' rows in the original layout, ' + (rows2.length - 1) + ' option rows, pack version ' + version);
