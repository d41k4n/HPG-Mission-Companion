# Voice packs

A voice pack is an XML file in `voicepacks\` next to the program (`Ctx.BaseDir`). It holds the texts of the mission messages, several options each. The mission sends the key of a message (`<say:CPL.5>`), the program picks the option, the actor and the radio and speaks it. The mission needs no table of texts, so dynamic parts (a dispatch, injuries, names) are passed as arguments and the texts can be changed or translated without a new mission. The user guide for mission authors is `docs/DEVELOPER_GUIDE.md` (also compiled into the program, About tab).

## Files and code

| Item | Role |
| --- | --- |
| `voicepacks\*.xml` (repository: `voicepacks/EN.xml`) | the packs; `build.cmd` copies the repository packs into the runtime folder only when they are not there yet |
| `src/VoicePacks.cs` | `VoicePack` and `PackMessage` model, `VoicePacks` (load, check, install with archiving) and `Say` (tag processing, option choice, placeholders, names, variety, version status) |
| `src/Engines.cs` `Player.EnqueueText` | calls `Say.Process` before `TagParser.Parse`; a sound-only message is played as a wav from the audio folder |
| `src/SimLink.cs` | publishes the status variables and watches the LVAR counters and the context (`SetWatch`, `OnWatch`) |
| `tools/import-pack.js` | one-shot importer from the voice messages spreadsheet (and `tools/pack-extras-EN.json`, draft texts for wavs with no row); after the import the XML is the source |
| `tests/voicepack-say-test.json`, `tests/voicepack-test-TST.xml` | standalone mission and test pack |

## XML format

```xml
<voicepack name="EN" language="EN" version="1" vcp="APP" title="...">   name optional (file name), language EN FR IT DE, version whole number >= 1, vcp = voice code of the mission in app mode (default APP)
  <names><name role="PLT">Pilot</name> ... <pool role="PLT">Mark, John, ...</pool></names>   job words of {PLT} {CPL} {PAX1} {PAX2} {PAX3} {TECHM} {TECHF}; the optional pools replace the static name lists for random names
  <message key="CPL.5" actor="2" radio="auto" group="" seq="0" sfx="0" lvar="L:CPL{VCP}5" wav="" sleep="" tight="" trigger="" draft="">
    <o>option text with {0} {PLT} {@PLT} and the normal tags</o>        six options (pack EN 6); <o sex="m"> / <o sex="f"> only for the male / female technician; <o silent="1"/> says nothing
  <message ... futile="1">                                          small talk nobody needs: options 1 and 2 silent, 3 the default, the group is always coherent
  </message>
</voicepack>
```

`key` is unique in the pack (letters, digits `. _ -`, up to 40). `actor` 1-11. `radio` auto (the mission decides), on or off (applies only when the mission sent no radio tag). `group`/`seq`: conversation for the coherent variety. A message with `sfx="1"` and a `wav` and no `<o>` plays the wav. `lvar` is the LVAR template read in app mode (see above; `{VCP}` is replaced by the `vcp` of the pack, messages without it are only reachable with `<say>`); `wav`, `sleep`, `tight`, `trigger`, `draft`, `new` are information for authors and tools (the wav of the same message, for the wav packs, the wav, seconds the mission waits after the message, options kept at a similar length, when the message happens, text written from a file name). The loader reports a problem for a bad key, actor or radio value, a duplicate key and a message with no option; such a message is skipped. A pack with no usable name or version 0 is not usable.

The messages of `EN.xml` use `ROLE.NUMBER` keys: the role is the LVAR prefix of the wav pack (PLT, CPL, PAX2, PAX3, FRA, SARA, MAR), `PAX1` for the LVARs with no prefix, `HW` for the emergency events (`L:<pack>HW<n>`), `SYS` for system sounds; `L:CPLANDREW5` is `CPL.5`.

## Voice app mode: the simulator variables (LVAR bridge)

The mission already has a mode for an external audio program: when `L:AUDIOINHIBIT` is not 0 it uses the voice code `APP` instead of the code of a wav pack, so a call such as `L:CPL{local:VCP}5 += 1` increments `L:CPLAPP5`, which no `sound.xml` knows: the aircraft plays no wav. The Companion sets `L:AUDIOINHIBIT` to 1 (option of the Service tab) and reads those counters through SimConnect (`SimLink.SetWatch`, groups of 90 variables, `SIM_FRAME` with the `CHANGED` flag): when a counter rises the message of the pack whose `lvar` template (`L:CPL{VCP}5`, `{VCP}` = the `vcp` attribute of the pack, default `APP`) matches is spoken. No call of the mission changes. The first value after a connection is the baseline (nothing is spoken), a reset to 0 is silent, a counter that rises by more than one in one packet speaks once. A line that has waited more than 30 s in the queue is dropped (`[DROP]`). The pack used is chosen in the Voice packs tab (`Settings.BridgePack`, the first pack when empty). Sounds without voice (the dispatch ringtones, the new message tone, the tablet alarm) are `sfx` messages: the wav of the audio folder is played at the master volume.

The checklists of the mission are plain `speak` texts with no tag. The mission sends `<defaultactor:11>` once when the voice code is `APP` (on the connection of the voice server): from then on every text without an actor tag is spoken by actor 11, the checklist voice (`Say.ApplyDefaultActor`); `<defaultactor:off>` ends it.

Dynamic text does not go through the counters: the mission uses `speak` (`<plain>`, `<say:KEY>args`) for it, the dispatcher reading the dispatch is one of them.

### Context for the radio rule

The counters carry no tags, so the program decides the radio itself from three variables that the mission publishes (macro `voice context publish`, called by the seat tracker loop while the crew monitor runs, only when `L:AUDIOINHIBIT` is not 0, only when the value changes):

| Variable | Meaning |
| --- | --- |
| `L:HMC_HELI_OFF` | 1: battery master off and rotor below 1 % |
| `L:HMC_CREW_PRESENT` | bit 0 pilot, 1 copilot, 2 doctor, 3 HEMS1, 4 HEMS2: aboard (seat tracker) |
| `L:HMC_CTX_VALID` | 1: the two above are published; 0 or never written: no radio tag is added |
| `L:HMC_TECH_SEX` | the technician of the scene: 0 male, 1 female (the mission picks one at random in app mode before the boarding and the arrival scenes); selects the options with `sex` and the voice (actor 6 or 7) |

Rule (`Say.AutoRadio`, only for messages with `radio="auto"` and when the mission sent no radio tag): the dispatcher (actor 8) is always on the radio; helicopter off: voice; speaker aboard (a crew actor whose bit is set): radio; speaker outside (Frank, Sarah, ground unit, patient, a crew member outside): radio when somebody of the crew is aboard, voice when everybody is outside.

## Placeholders for lists of vehicles

`{list:N}` and `{be:N}` take argument N made of the letters A (ambulance), F (fire engine), P (police): `AFP` gives "ambulance, fire engine and police" and "are", `P` gives "police" and "is". The words come from the `<names>` of the pack (`VEH_A`, `VEH_F`, `VEH_P`, `AND`, `IS`, `ARE`) so that a pack of another language can use its own. The mission sends the letters as arguments: `<say:DSP.VEH.BOTH>A//FP` (argument 0 the vehicles on the way, argument 1 the vehicles on scene).

## Volume (Mixer)

The level of a line is: the volume tag of the text, times the volume of the mission (`L:VOLUME_CREW` for the crew and the sound files, `L:VOLUME_CHK` for actor 11; option "Follow the volumes of the mission"), times the position factor, times the master volume. The position factor applies to voices, not to radio calls: with the view outside the helicopter (`CAMERA STATE` other than 2) a speaker aboard is reduced by `AttenCamOutside` dB (default 12); with the view inside a speaker outside is reduced by `AttenCamInside` dB (default 9). Who is aboard comes from `L:HMC_CREW_PRESENT` (the dispatcher, the checklist voice, an unknown speaker or no context: no reduction). The factors are applied to the wav of each line when its turn comes (`Player.Speak`), never while it plays. All three variables are watched like the counters.

## Crew names and variety

`<names:random>` (the mission sends it at a new shift) draws one name for each of the roles PLT, CPL, PAX1, PAX2, PAX3, TECHM, TECHF from the static lists of first names of `src/Names.cs` (the first names of the patients of the mission; a `<pool role="..">` of the pack replaces the list for that role; the male technician takes a male name, the female one a female name), never the same name twice, but only when the user chose "Random names for each shift" in the Voice packs tab; otherwise the names typed in the Actors tab (Name boxes of actors 1-7) stay, and a role without a name keeps its job word (Pilot, Copilot, Chief, Paramedic, Doc, Technician). `{PLT}` is always the job word; `{@PLT}` is the name or, without one, the job word. `{TECH}` / `{@TECH}` is the technician of the scene (`L:HMC_TECH_SEX`). Options with `sex="m"` or `sex="f"` are used only for that technician, and a message that has them is spoken by actor 6 (Technician male) or 7 (Technician female); without the variable the sex the message was written for (actor 6: male, 7: female) is used. `Settings.Variety` (random or coherent) is the default of `<variety:..>` and works for the counters too, which carry no tags.

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

## Six options, small talk and who can speak

Pack EN 6 has six options for every message. Option 1 is the default (the wav text, corrected) and stays so; 2 and 3 are professional variants; 4 to 6 are more and more colloquial where the sentence allows it, so that a filter on the option number can later cut the small talk. A futile message (`futile="1"`: greetings, jokes, small talk nobody needs) starts at 3: options 1 and 2 are silent (`<o silent="1"/>`), 3 is the default, 4 to 6 the others, and the lines of a group of futile messages are always coherent, so a silent option silences the whole conversation. Callouts (numbers, clock positions, Vy, Vtoss, VNE) repeat the same word on purpose. A message with options for the technicians has six for each sex (a futile one has two silent options shared by both and four for each sex).

The dead and the badly hurt do not speak: the mission publishes `L:HMC_CREW_MUTE` (bit 0 pilot, 1 copilot, 2 doctor, 3 HEMS1, 4 HEMS2, as `L:HMC_CREW_PRESENT`) for the crew members that are dead or have a life score under 15, and `Say` keeps the lines of that actor silent (log: `not said`). The mission decides the small talk too (macro voice calm): never with a dying patient, an injured crew member, after a death or in an emergency.

The chatter level: the mission publishes `L:HMC_CHAT_MAX` (its setting CREW CHATTER: FULL 6, REDUCED 3, MINIMAL 1; not published or 0 means 6) and the program never picks an option number above it (`Pick`: the number of options of the message, or of the technician, is cut to that value before the choice; a coherent group keeps its number, cut to the same limit). Because the professional wording has the low numbers and a futile message starts with two silent options, REDUCED lets a futile line through only at its default and MINIMAL says the default wording everywhere and keeps the futile lines silent. The variable is part of the context the mission publishes only when it changes (`voice context publish`).

The pack is edited from `tools/variants-EN/*.json` with `tools/build-variants.js`; `tools/export-xlsx.js` writes the voice messages spreadsheet from it.
