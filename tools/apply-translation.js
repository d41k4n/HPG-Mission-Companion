#!/usr/bin/env node

'use strict';

// Applies translated messages from a JSON file into a target voicepack XML (e.g. voicepacks/FR.xml, voicepacks/DE.xml)
// using voicepacks/EN.xml (or any specified base pack) as the template.
//
// Usage:
//   node tools/apply-translation.js <path/to/translation.json> [TARGET_LANG] [OUTPUT_XML] [BASE_XML]
//
// Examples:
//   node tools/apply-translation.js tools/translation-FR.json
//   node tools/apply-translation.js tools/translation-IT.json IT
//   node tools/apply-translation.js tools/translation-DE.json
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');

if (!process.argv[2]) {
  console.log(`Usage: node tools/apply-translation.js <path/to/translation.json> [TARGET_LANG] [OUTPUT_XML] [BASE_XML]`);
  console.log(`Examples:`);
  console.log(`  node tools/apply-translation.js tools/translation-FR.json`);
  console.log(`  node tools/apply-translation.js tools/translation-DE.json`);
  process.exit(1);
}

const inputJsonPath = path.resolve(process.argv[2]);
if (!fs.existsSync(inputJsonPath)) {
  console.error(`Translation JSON file not found: ${inputJsonPath}`);
  process.exit(1);
}

const translation = JSON.parse(fs.readFileSync(inputJsonPath, 'utf8'));

// Determine target language / pack name
let targetLang = process.argv[3];
if (!targetLang) {
  if (translation.target_pack) targetLang = translation.target_pack;
  else if (translation.language) targetLang = translation.language;
  else {
    // Try inferring from filename (e.g. translation-FR.json -> FR)
    const baseName = path.basename(inputJsonPath, '.json');
    const m = baseName.match(/[-_]([A-Za-z]{2,5})$/);
    targetLang = m ? m[1] : 'DE';
  }
}
targetLang = targetLang.trim().toUpperCase();

// Determine source template pack
const sourcePack = (process.argv[5] || translation.source_pack || 'EN').trim().toUpperCase();
const templateXmlPath = path.join(root, 'voicepacks', `${sourcePack}.xml`);
if (!fs.existsSync(templateXmlPath)) {
  console.error(`Template voicepack not found: ${templateXmlPath}`);
  process.exit(1);
}

// Determine output XML path
const outputXmlPath = process.argv[4] ? path.resolve(process.argv[4]) : path.join(root, 'voicepacks', `${targetLang}.xml`);

const baseXml = fs.readFileSync(templateXmlPath, 'utf8');

// Extract base version from template
const baseVerMatch = baseXml.match(/<voicepack\b[^>]*\bversion="(\d+)"/);
const baseVer = baseVerMatch ? baseVerMatch[1] : '1';

// Standard titles for common languages
const titles = {
  DE: "German crew voices of the HEMS missions",
  FR: "French crew voices of the HEMS missions",
  IT: "Italian crew voices of the HEMS missions",
  ES: "Spanish crew voices of the HEMS missions",
  NL: "Dutch crew voices of the HEMS missions",
  CH: "Swiss German crew voices of the HEMS missions"
};
const packTitle = translation.title || titles[targetLang] || `${targetLang} crew voices of the HEMS missions`;

// Build map of translated messages: key -> array of option texts
const transMap = new Map();
for (const msg of (translation.messages || [])) {
  if (msg.key && Array.isArray(msg.options)) {
    transMap.set(msg.key, msg.options.map((opt) => typeof opt === 'string' ? opt : (opt.text || '')));
  }
}

// 1. Update root voicepack tag
let outXml = baseXml.replace(
  /<voicepack\b[^>]*>/,
  `<voicepack name="${targetLang}" language="${targetLang}" version="1" basever="${baseVer}" vcp="APP" title="${packTitle}">`
);

// 2. Update <names> section if provided
if (translation.names) {
  outXml = outXml.replace(/<names>([\s\S]*?)<\/names>/, (match, inner) => {
    let updated = inner;
    for (const [role, val] of Object.entries(translation.names)) {
      const rx = new RegExp(`(<name role="${role}">)[^<]*(</name>)`);
      updated = updated.replace(rx, `$1${val}$2`);
    }
    return `<names>${updated}</names>`;
  });
}

// 3. Replace <o> texts for each message
let replacedCount = 0;
outXml = outXml.replace(/<message\b([^>]*)>([\s\S]*?)<\/message>/g, (msgFull, attrs, body) => {
  const keyMatch = attrs.match(/\bkey="([^"]+)"/);
  if (!keyMatch) return msgFull;
  const key = keyMatch[1];

  const transOpts = transMap.get(key);
  if (!transOpts) return msgFull; // Keep template if not translated

  let optIdx = 0;
  const newBody = body.replace(/<o\b([^>]*?)(?:\/>|>([^<]*)<\/o>)/g, (oFull, oAttrs, oText) => {
    const curIdx = optIdx++;
    const isSilent = /\bsilent="1"/.test(oAttrs);
    if (isSilent) return oFull; // Keep silent options unchanged
    const newText = transOpts[curIdx];
    if (newText === undefined || newText === '') return oFull;
    return `<o${oAttrs}>${newText}</o>`;
  });

  replacedCount++;
  return `<message${attrs}>${newBody}</message>`;
});

fs.writeFileSync(outputXmlPath, outXml, 'utf8');
console.log(`Applied translations for ${replacedCount} messages from ${inputJsonPath}`);
console.log(`Generated: ${outputXmlPath} (language: ${targetLang}, basever: ${baseVer})`);
