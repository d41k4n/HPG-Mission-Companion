#!/usr/bin/env node

'use strict';

// Exports voicepack texts from voicepacks/EN.xml (or any base pack) to a structured JSON file
// suitable for translating into other languages (e.g. DE, FR, IT, ES) via an LLM or translation API.
//
// Usage:
//   node tools/export-for-translation.js [TARGET_LANG] [OUTPUT_JSON] [SOURCE_PACK]
//
// Examples:
//   node tools/export-for-translation.js FR
//   node tools/export-for-translation.js IT tools/my-italian-source.json
//   node tools/export-for-translation.js DE
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');

const targetLang = (process.argv[2] || 'DE').trim().toUpperCase();
const sourcePack = (process.argv[4] || 'EN').trim().toUpperCase();
const defaultOutName = `translation-source-${targetLang}.json`;
const outPath = process.argv[3] ? path.resolve(process.argv[3]) : path.join(root, 'tools', defaultOutName);
const xmlPath = path.join(root, 'voicepacks', `${sourcePack}.xml`);

if (!fs.existsSync(xmlPath)) {
  console.error(`Source voicepack not found: ${xmlPath}`);
  process.exit(1);
}

const text = fs.readFileSync(xmlPath, 'utf8');

const placeholdersGuide = {
  "{0}, {1}": "Positional arguments from the simulator/mission (e.g. ETA, altitude, patient injury, street address, vehicle color). Keep unchanged.",
  "{@PLT}": "Name of the Pilot (e.g. 'Markus') or fallback job title. Keep unchanged.",
  "{@CPL}": "Name of the Copilot or fallback job title. Keep unchanged.",
  "{@PAX3}": "Name of the Doctor / Notarzt or fallback job title. Keep unchanged.",
  "{@TECH}": "Name of the scene Technician (male or female) or fallback job title. Keep unchanged.",
  "{PLT}, {CPL}, {TECH}": "Role title nouns from <names>. Keep unchanged.",
  "{list:0}, {list:1}": "Formatted list of emergency vehicles (Ambulance, Fire, Police). Keep unchanged.",
  "{be:0}, {be:1}": "Verb agreement ('is' or 'are') matching vehicle list count. Keep unchanged."
};

// Parse names
const names = {};
const nameMatches = text.matchAll(/<name role="([^"]+)">([^<]*)<\/name>/g);
for (const m of nameMatches) {
  names[m[1]] = m[2];
}

// Parse messages
const messages = [];
const msgRx = /<message\b([^>]*)>([\s\S]*?)<\/message>/g;
let msgMatch;

while ((msgMatch = msgRx.exec(text)) !== null) {
  const attrs = msgMatch[1];
  const body = msgMatch[2];

  const keyMatch = attrs.match(/\bkey="([^"]+)"/);
  if (!keyMatch) continue;
  const key = keyMatch[1];

  const sfxMatch = attrs.match(/\bsfx="1"/);
  if (sfxMatch) continue; // Sound-only message, no speech

  const actorMatch = attrs.match(/\bactor="(\d+)"/);
  const actor = actorMatch ? parseInt(actorMatch[1], 10) : 1;

  const options = [];
  const optRx = /<o\b([^>]*?)(?:\/>|>([^<]*)<\/o>)/g;
  let optMatch;
  while ((optMatch = optRx.exec(body)) !== null) {
    const optAttrs = optMatch[1] || '';
    const optContent = optMatch[2] !== undefined ? optMatch[2] : '';
    const isSilent = /\bsilent="1"/.test(optAttrs);
    const sexMatch = optAttrs.match(/\bsex="([mf])"/);
    const sex = sexMatch ? sexMatch[1] : null;

    options.push({
      text: optContent,
      silent: isSilent,
      sex: sex
    });
  }

  messages.push({
    key: key,
    actor: actor,
    options: options
  });
}

const exportData = {
  source_pack: sourcePack,
  target_pack: targetLang,
  placeholders_guide: placeholdersGuide,
  names: names,
  total_messages: messages.length,
  messages: messages
};

fs.writeFileSync(outPath, JSON.stringify(exportData, null, 2), 'utf8');
console.log(`Exported ${messages.length} messages from ${sourcePack}.xml for target language '${targetLang}' to:`);
console.log(`  ${outPath}`);
