# Voice packs

A voice pack is an XML file in `voicepacks\` next to the program (`Ctx.BaseDir`). It holds the texts of the mission messages, several options each. The mission sends the key of a message (`<say:CPL.5>`), the program picks the option, the actor and the radio and speaks it. The mission needs no table of texts, so dynamic parts (a dispatch, injuries, names) are passed as arguments and the texts can be changed or translated without a new mission. The user guide for mission authors is `docs/DEVELOPER_GUIDE.md` (also compiled into the program, About tab).

## Files and code

| Item | Role |
| --- | --- |
| `voicepacks\*.xml` (repository: `voicepacks/EN.xml`) | the packs; `build.cmd` copies the repository packs into the runtime folder only when they are not there yet |
| `src/VoicePacks.cs` | `VoicePack` and `PackMessage` model, `VoicePacks` (load, check, install with archiving) and `Say` (tag processing, option choice, placeholders, names, variety, version status) |
| `src/Engines.cs` `Player.EnqueueText` | calls `Say.Process` before `TagParser.Parse`; a sound-only message is played as a wav from the audio folder |
| `src/SimLink.cs` | publishes the status variables |
| `tools/import-pack.js` | one-shot importer from the voice messages spreadsheet (and `tools/pack-extras-EN.json`, draft texts for wavs with no row); after the import the XML is the source |
| `tests/voicepack-say-test.json`, `tests/voicepack-test-TST.xml` | standalone mission and test pack |

## XML format

```xml
<voicepack name="EN" language="EN" version="1" title="...">      name optional (file name), language EN FR IT DE, version whole number >= 1
  <names><name role="PLT">Captain</name> ...</names>             default text of {PLT} {CPL} {PAX1} {PAX2} {PAX3}
  <message key="CPL.5" actor="2" radio="auto" group="" seq="0" sfx="0" lvar="" wav="" sleep="" tight="" trigger="" draft="">
    <o>option text with {0} {PLT} and the normal tags</o>        any number of options
  </message>
</voicepack>
```

`key` is unique in the pack (letters, digits `. _ -`, up to 40). `actor` 1-11. `radio` auto (the mission decides), on or off (applies only when the mission sent no radio tag). `group`/`seq`: conversation for the coherent variety. A message with `sfx="1"` and a `wav` and no `<o>` plays the wav. `lvar`, `wav`, `sleep`, `tight`, `trigger`, `draft` are information for authors and tools (the LVAR of the wav pack, the wav, seconds the mission waits after the message, options kept at a similar length, when the message happens, text written from a file name). The loader reports a problem for a bad key, actor or radio value, a duplicate key and a message with no option; such a message is skipped. A pack with no usable name or version 0 is not usable.

The messages of `EN.xml` use `ROLE.NUMBER` keys: the role is the LVAR prefix of the wav pack (PLT, CPL, PAX2, PAX3, FRA, SARA, MAR), `PAX1` for the LVARs with no prefix, `HW` for the emergency events (`L:<pack>HW<n>`), `SYS` for system sounds; `L:CPLANDREW5` is `CPL.5`.

## Choosing the option

Random (default): any option except the one used last time for the same key. Coherent (`<variety:coherent>`): for a message with a `group`, the option number is drawn when the group starts (another group, or more than 180 s since the last message of the group) and kept for the following messages of the group (limited to the options each message has); messages without a group stay random. Options whose number does not exist for a message fall back to its last option.

## Versions

Every change of a pack's texts raises its version. `<pack:NAME:VER>` declares the pack the mission uses and the version it needs; the program compares with the installed pack and publishes through SimConnect:

| Variable | Value |
| --- | --- |
| `L:HMC_VOICE_STATUS` | 0 nothing declared, 1 found and not older than needed, 2 not installed, 3 older than the mission needs (obsolete) |
| `L:HMC_VOICEPACK_VERSION` | version of the declared pack (0 when missing) |
| `L:HMC_APP_VERSION` | version of the program |

The variables are written at every connection, every 5 s and when they change. A newer pack than needed is accepted. Installing a pack of the same name moves the old file to `voicepacks\obsolete\<name>-v<version>-<time>.xml`; an equal or lower version asks first. If `<say>` finds no pack with the key, the log has a warning and nothing is spoken. If the declared pack is missing, other installed packs are searched for the key.

## Installing

Drag the XML onto the Voice packs tab, Browse, or `--install-pack file.xml`; the copy is saved as `<pack name>.xml`. When the file has no `name` attribute the file name (letters, digits, `_`, `-`) is the pack name.