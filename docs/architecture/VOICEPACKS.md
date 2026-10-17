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
| `tools/sync-pack-texts.js` | copies the texts of the mission (`tools/voice-texts.json` of the mission repository) into the generated block of `EN.xml` (`--check` only compares) |
| `tests/voicepack-test-TXT.xml`, `tests/text-keys-lines.txt` | test pack and `--expand` scenario of `<text:KEY>` and of the translation rules |

## XML format

```xml
<voicepack name="EN" language="EN" version="1" basever="9" minapp="0.13" vcp="APP" title="...">   name optional (file name), language EN FR IT DE, version whole number >= 1, basever (optional) = version of the base pack EN a translation was made from, minapp (optional) = oldest program version, vcp = voice code of the mission in app mode (default APP)
  <names><name role="PLT">Pilot</name> ... <pool role="PLT">Mark, John, ...</pool></names>   job words of {PLT} {CPL} {PAX1} {PAX2} {PAX3} {TECHM} {TECHF}; the optional pools replace the static name lists for random names
  <message key="CPL.5" actor="2" radio="auto" group="" seq="0" sfx="0" lvar="L:CPL{VCP}5" wav="" sleep="" tight="" trigger="" draft="">
    <o>option text with {0} {PLT} {@PLT} and the normal tags</o>        six options (pack EN 6); <o sex="m"> / <o sex="f"> only for the male / female technician; <o silent="1"/> says nothing
  <message ... futile="1">                                          small talk nobody needs: options 1 and 2 silent, 3 the default, the group is always coherent
  </message>
</voicepack>
```

`key` is unique in the pack (letters, digits `. _ -`, up to 40). `actor` 1-11. `radio` auto (the program decides from the context of the mission, see below), on (radio call), off (voice), ics (intercom), ext (external radio call, heard only when the radio is on), ext-near (external call from somebody next to the crew, always heard); the values other than auto apply only when the mission sent no radio tag. `group`/`seq`: conversation for the coherent variety. A message with `sfx="1"` and a `wav` and no `<o>` plays the wav; `vol="0-100"` on it caps its level (the tablet alarm has 70), the master volume and the volume of the mission still apply. `lvar` is the LVAR template read in app mode (see above; `{VCP}` is replaced by the `vcp` of the pack, messages without it are only reachable with `<say>`); `wav`, `sleep`, `tight`, `trigger`, `draft`, `new` are information for authors and tools (the wav of the same message, for the wav packs, the wav, seconds the mission waits after the message, options kept at a similar length, when the message happens, text written from a file name). The loader reports a problem for a bad key, actor or radio value, a duplicate key and a message with no option; such a message is skipped. A pack with no usable name or version 0 is not usable.

The messages of `EN.xml` use `ROLE.NUMBER` keys: the role is the LVAR prefix of the wav pack (PLT, CPL, PAX2, PAX3, FRA, SARA, MAR), `PAX1` for the LVARs with no prefix, `HW` for the emergency events (`L:<pack>HW<n>`), `SYS` for system sounds; `L:CPLANDREW5` is `CPL.5`.

## Voice app mode: the simulator variables (LVAR bridge)

The mission already has a mode for an external audio program: when `L:AUDIOINHIBIT` is not 0 it uses the voice code `APP` instead of the code of a wav pack, so a call such as `L:CPL{local:VCP}5 += 1` increments `L:CPLAPP5`, which no `sound.xml` knows: the aircraft plays no wav. The Companion sets `L:AUDIOINHIBIT` to 1 (option of the Service tab) and reads those counters through SimConnect (`SimLink.SetWatch`, groups of 90 variables, `SIM_FRAME` with the `CHANGED` flag): when a counter rises the message of the pack whose `lvar` template (`L:CPL{VCP}5`, `{VCP}` = the `vcp` attribute of the pack, default `APP`) matches is spoken. No call of the mission changes. The first value after a connection is the baseline (nothing is spoken), a reset to 0 is silent, a counter that rises by more than one in one packet speaks once. A line that has waited more than 30 s in the queue is dropped (`[DROP]`). The pack used is chosen in the Voice packs tab, box "Active pack used by the mission" (`Settings.BridgePack`, the first pack when empty). The counter names carry the `vcp` of the pack, never its language, so the mission is the same for every language: the pack chosen here decides the language of the lines. The pack of the tab also comes first for `<say:KEY>` and `<text:KEY>` (see below); a pack that the mission declares with `<pack:NAME:VER>` is used for the version check and, for `<say:KEY>`, as the second choice (a key the active pack lacks is spoken with the text of the declared pack and the log says `[UNTRANSLATED]`), but it does not change the pack read from the counters: only the choice of the tab does. Sounds without voice (the dispatch ringtones, the new message tone, the tablet alarm) are `sfx` messages: the wav of the audio folder is played at the master volume.

The checklists of the mission are `speak` texts with a key and their English (`<text:CHK.macro.nn>English`, see "Texts of the mission"). The mission sends `<defaultactor:11>` once when the voice code is `APP` (on the connection of the voice server): from then on every text without an actor tag is spoken by actor 11, the checklist voice (`Say.ApplyDefaultActor`); `<defaultactor:off>` ends it.

Dynamic text does not go through the counters: the mission uses `speak` (`<plain>`, `<say:KEY>args`) for it, the dispatcher reading the dispatch is one of them.

### Context for the radio rule

The counters carry no tags, so the program decides how a line is heard itself from the variables that the mission publishes (macro `voice context publish`, called by the seat tracker loop while the crew monitor runs, only when `L:AUDIOINHIBIT` is not 0, only when the value changes):

| Variable | Meaning |
| --- | --- |
| `L:HMC_HELI_OFF` | 1: battery master off and rotor below 1 % |
| `L:HMC_CREW_PRESENT` | bit 0 pilot, 1 copilot, 2 doctor, 3 HEMS1, 4 HEMS2: aboard (seat tracker) |
| `L:HMC_CREW_EXIST` | the same bits: the crew members that exist, aboard or outside (0 = not published: a speaker aboard is on the radio, the old rule) |
| `L:H145_SDK_OH_BATTERY_MASTER` | not published by the mission: the program reads the variable of the aircraft (1 = battery master on, the radio works); not read: the opposite of `L:HMC_HELI_OFF` |
| `L:HMC_AT_BASE` | 1: the helicopter is at the departure base, in range of its wifi: the dispatcher reaches the crew radios even with the battery off |
| `L:HMC_ZONE_TECH`, `L:HMC_ZONE_GROUND`, `L:HMC_ZONE_PATIENT` | distance of the technician (actors 6, 7), of the ground unit and the hospital staff (actor 9), of the patient / extra (actor 10) from the helicopter: 1 up to 5 m (next to it), 2 up to 30 m, 3 up to 100 m, 4 beyond, 0 not published (counts as 2) |
| `L:HMC_CTX_VALID` | 1: the context is published; 0 or never written: no radio tag is added |
| `L:HMC_TECH_SEX` | the technician of the scene: 0 male, 1 female (the mission picks one at random in app mode before the boarding and the arrival scenes); selects the options with `sex` and the voice (actor 6 or 7) |

Rule (`Say.Decide`, only for messages with `radio="auto"` and when the mission sent no radio tag; the rules of the user of 2026-10-11, the numbers are in `RadioRules`):

| Speaker | Heard as |
| --- | --- |
| Dispatcher (actor 8; also the plain `speak` texts of the dispatcher) | external radio call (double beep, hiss + 20) when the battery is on, or with the helicopter at the base (`L:HMC_AT_BASE`: the crew radios); otherwise nothing (the tablet tone only) |
| Crew (actors 1-5), somebody of the crew is outside | radio call (beep of the user, hiss + 10) whatever the battery: the crew carries radios |
| Crew, everybody aboard | intercom (ICS: click, no beep, the hiss of the user) with the battery on; voice with the battery off |
| Crew outside, nobody aboard | voice at the normal level, in the air, whatever the view (everybody is outside together; the simmer loses nothing) |
| Technician (6, 7) next to the helicopter (zone 1), pilot aboard, battery on | intercom |
| Technician, ground unit, patient, otherwise | voice; heard from inside at the normal level up to 30 m, very low (-20 dB) up to 100 m, not beyond; nobody aboard: normal level |

A message that must always sound the same sets `radio` in the pack: the replies of the hospital while inbound (`HOS.REPLY*`) are `ext`, the hospital team at the helipad (`HOS.WEL`, `HOS.ORG`, `HOS.THX`) `ext-near`. The camera attenuation of the Volume tab still applies to voices when somebody is aboard; radio and intercom are not attenuated. A plain text with an explicit `<radio>`, `<ics>` or `<radio:ext>` tag keeps the tag, except the dispatcher.

## Placeholders for lists of vehicles

`{list:N}` and `{be:N}` take argument N made of the letters A (ambulance), F (fire engine), P (police): `AFP` gives "ambulance, fire engine and police" and "are", `P` gives "police" and "is". The words come from the `<names>` of the pack (`VEH_A`, `VEH_F`, `VEH_P`, `AND`, `IS`, `ARE`) so that a pack of another language can use its own. The mission sends the letters as arguments: `<say:DSP.VEH.BOTH>A//FP` (argument 0 the vehicles on the way, argument 1 the vehicles on scene).

## Volume (Mixer)

The level of a line is: the volume tag of the text, times the volume of the mission (`L:VOLUME_CREW` for the crew and the sound files, `L:VOLUME_CHK` for actor 11; option "Follow the volumes of the mission"), times the position factor, times the master volume. The position factor applies to voices, not to radio calls: with the view outside the helicopter (`CAMERA STATE` other than 2) a speaker aboard is reduced by `AttenCamOutside` dB (default 12); with the view inside a speaker outside is reduced by `AttenCamInside` dB (default 9). Who is aboard comes from `L:HMC_CREW_PRESENT` (the dispatcher, the checklist voice, an unknown speaker or no context: no reduction). The factors are applied to the wav of each line when its turn comes (`Player.Speak`), never while it plays. The tests of the program itself (the Test buttons of the Actors, Piper voices and Radio effect tabs, "Try a text" of the Service tab; `Player.EnqueueTest`) ignore the volume of the mission and the inside / outside level, as if `L:VOLUME_CREW` were 100: they must be audible with no simulator and no mission running (only the tag volume and the master volume apply). All three variables are watched like the counters.

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

The variables are written at every connection, every 5 s and when they change. A newer pack than needed is accepted. Installing a pack of the same name moves the old file to `voicepacks\obsolete\<name>-v<version>-<time>.xml`; an equal or lower version asks first. If `<say>` finds no pack with the key, the log has a warning and nothing is spoken. The search order for `<say:KEY>` is the active pack (Voice packs tab), the pack the mission declared, then any other installed pack.

## Texts of the mission (`<text:KEY>`)

Some texts the mission speaks are not messages of the crew but free texts that the mission already has in English: the rows of the checklists (377) and the dispatch descriptions (288 incident descriptions). They are written in the mission as `<text:KEY>English text` (`{"speak":"<text:CHK.interiorckl.02>Baggage, cargo and loose items: stowed and secured!"}`; for a description `<actor8><radio><text:DSC.17.2>` plus the description, the key built from the incident `ID_CARD` and the variant). The tags before it (actor, radio, `<defaultactor>`) stay the mission's. `Say.ExpandText` looks for a message with that key in the **active pack** (the one of the Voice packs tab) and, when it has one, speaks its first option instead of the English; the optional values after the English (`<text:DSC.VEH>Vehicle: red Toyota.//red//Toyota`) fill `{0} {1}` of that option. Without the key in the pack the English text is spoken as the mission sends it, and the log says `[UNTRANSLATED] KEY is not in the pack X` once for each key. Safeguard: the base pack (EN) holds the same English under the same key; when the text the mission sends is not the one of the base pack (a stale key, or a base pack older than the mission) the English of the mission is spoken and the log says so, never a sentence that does not fit.

A message of this kind has `text="1"` and exactly one `<o>`; the actor, the radio and the other attributes are information only (the mission decides who speaks and how). The block of these messages in `EN.xml` sits between the markers `MISSION TEXTS BEGIN` and `MISSION TEXTS END` and is generated: **do not edit it by hand**. The mission lists its texts in `tools/voice-texts.json` (`node tools/voice-texts.js sync` in the mission repository, which also gives a key to every new checklist row with `assign`); here `node tools/sync-pack-texts.js` copies them into `voicepacks/EN.xml` and raises the pack version. `tools/check.js` fails when the two are out of step (when the mission repository is cloned next to this one). The rule for new missions or new texts: change the text in the mission, run `assign` and `sync` there, run `sync-pack-texts.js` here, build, and tell the translators: the packs that lack the new keys turn red in the list.

## Names of the dispatcher, call sign of the helicopter, heli rescuer (pack EN 14)

- **Dispatcher.** The mission publishes who the dispatcher is with `<dsp:INDEX:CITY>` (index of the group chosen on the CARLS in the static `GRPID`, city of the base found by the mission with an Overpass query; the last tag stays until another comes). The pack holds the words: `DSP.NAME.<index>` (with `{0}` the city), `DSP.NAMEX.<index>` (the same without the city, used when the city is unknown; when it is missing `DSP.NAME.<index>` is used) and `DSP.NAME.MAN` for the manual frequency. They are messages with `text="1"` and one option. `{DSP}` and `{@DSP}` in any text become those words; before the first tag they stay "dispatch". The names of the groups are in the pack, so a translation can change them.
- **Call sign.** `{HEMS}` and `{@HEMS}` become the call sign of the helicopter, by the setting *Call sign of the helicopter* (Actors tab): *Airline of the aircraft* (the `ATC AIRLINE` of the aircraft, the atc_airline of the aircraft.cfg of the livery, read through SimConnect every 30 s: "PELIKAN 3" is said "Pelikan 3") or *Custom name* (the text box). With no airline, or an empty custom name, it is HEMS. `--atc-test <report>` reads the three ATC variables from the running simulator.
- **Voices at random.** The actors 8 (dispatcher), 9 (ground unit) and 12 (heli rescuer) can have the voice *Random voice* with a pool (`ActorSetting.Random`, `Pool`). `VoiceDraw` keeps one voice for each until the mission sends `<voices:shift>` (the dispatcher), `<voices:dispatch>` (ground unit and heli rescuer) or `<voices:all>`; the drawn voices of the actors are never the same, and avoid the fixed voices of the other actors and the voice they had before.
- **Lines to the technician.** A message with `to="tech"` (and the keys CPL.23, PAX1.37, PAX1.38, PAX2.2, PAX2.3, PLT.5, PLT.9, PLT.21, PLT.26 in any pack) is a crew line said to the technician: intercom when the technician is next to the helicopter, the speaker is aboard and the battery is on; otherwise voice at the distance of the technician. Never a radio call.
- **Dispatch with the crew away.** When the mission publishes that nobody of the crew (the pilot excluded) is aboard, the dispatcher and the lines of the dispatch decision (`DSA.*`, `DSP.*` but the names) are not said: the tablet sound is enough. They come back when a crew member is aboard again.
- **Heli rescuers.** Actor 12 is the Heli rescuer (voice and name in the Actors tab). The helpers `{hr:N}` (N = 1 or 2: the names `HR_1` and `HR_2` of the pack, "one heli rescuer", "two heli rescuers") and `{place:N}` (N = H, B or M: `PL_H` the hospital, `PL_B` the base, `PL_M` the meeting point) take the words from `<names>`, like `{list}` and `{be}`.
- Test lines for `--expand`: `tests/dsp-and-callsign-lines.txt` (`@atc ID|AIRLINE|NUMBER` feeds the aircraft data, `@settings callsign=Name_With_Underscores`).

## Translations and the base pack

The base pack is `EN`. A translated pack is a copy of it: the same keys, the same `{0} {1}` values and tags, the texts in another language; `language=""` says which one (EN FR IT DE) and the voices of the actors must be voices of that language (Actors and voices tab). New translation (Voice packs tab, or `--pack-template <file.xml> --lang IT [--name IT]`) writes the base pack as the start of a new file with `basever=""` set to the version of EN it was made from. `basever` and `minapp` (the oldest program that can use the pack, for example `minapp="0.13"`) are optional attributes of `<voicepack>`.

The Voice packs tab compares every pack with EN (`PackCheck.Compare`) and writes the result in the State column; the row is **red** when the pack lacks keys of the base pack (they are spoken in English), when it was made for a newer base pack than this program has (`basever` above the version of EN: update the program), when it needs a newer program (`minapp`), or when it is older than the version the mission asks for in `<pack:NAME:VER>`; it is **orange** when its texts use other `{values}` than the base pack or the base pack moved on (`basever` below the version of EN, no key missing). The same text is in the log at every start, in the answer of Install and in `--packs-check`, together with the number of texts that are still the English ones. The button Add missing texts (or `--pack-add-missing <pack>`) appends the missing messages in English at the end of the selected pack, sets `basever` to the version of EN and raises the pack version; the old file goes to `voicepacks\obsolete`.

## Updating the base pack apart from the program

The Updates tab reads the release with the tag `voicepack-EN` of the program repository (`Updates.PackReleaseApi`): its asset `EN.xml` is downloaded to the temporary folder and read by the pack loader. When its `version` is higher than the installed pack of the same name and its `minapp` is not newer than the program (`Updates.PackIsNewer`), the check offers it: the footer button says Update: pack (or mission+pack, program+pack, all), the question shows the notes of that release and `VoicePacks.Install` replaces only the XML (the old one goes to `voicepacks\obsolete`, no question is asked about a newer version). When the program itself is updated the pack is not installed apart: the setup carries the pack of its release. The check box Also the voice pack EN (setting `UpdatePack`, on by default) switches it off. Texts of messages that the mission already calls can be published this way; a new trigger (a message nobody calls yet) needs a new mission build as well.

## Installing

Drag the XML onto the Voice packs tab, Browse, or `--install-pack file.xml`; the copy is saved as `<pack name>.xml`. When the file has no `name` attribute the file name (letters, digits, `_`, `-`) is the pack name.

## Six options, small talk and who can speak

Pack EN 6 has six options for every message. Option 1 is the default (the wav text, corrected) and stays so; 2 and 3 are professional variants; 4 to 6 are more and more colloquial where the sentence allows it, so that a filter on the option number can later cut the small talk. A futile message (`futile="1"`: greetings, jokes, small talk nobody needs) starts at 3: options 1 and 2 are silent (`<o silent="1"/>`), 3 is the default, 4 to 6 the others, and the lines of a group of futile messages are always coherent, so a silent option silences the whole conversation. Callouts (numbers, clock positions, Vy, Vtoss, VNE) repeat the same word on purpose. A message with options for the technicians has six for each sex (a futile one has two silent options shared by both and four for each sex).

The dead and the badly hurt do not speak: the mission publishes `L:HMC_CREW_MUTE` (bit 0 pilot, 1 copilot, 2 doctor, 3 HEMS1, 4 HEMS2, as `L:HMC_CREW_PRESENT`) for the crew members that are dead or have a life score under 15, and `Say` keeps the lines of that actor silent (log: `not said`). The mission decides the small talk too (macro voice calm): never with a dying patient, an injured crew member, after a death or in an emergency.

A reduced crew: the mission publishes `L:HMC_CREW_EXIST` (the same bits, the members this flight has: pilot always, doctor with 2 crew or more, copilot with 3, HEMS1 with 4, HEMS2 with 5). A text message (it has options) of a seat the crew does not have is said by a member who is there, with that member's voice, in this order: copilot to pilot, doctor, HEMS1, HEMS2; doctor to HEMS1, HEMS2, copilot, pilot; HEMS1 to doctor, HEMS2, copilot, pilot; HEMS2 to HEMS1, doctor, copilot, pilot; a member who cannot speak (`L:HMC_CREW_MUTE`) is skipped, and when nobody can the line is not said (log: `is not in this crew`). The messages that are only a wav file are not changed. With `L:HMC_CREW_EXIST` not published (0) nothing is replaced. Scenario: `tests/crew-standin-lines.txt`.

The chatter level: the mission publishes `L:HMC_CHAT_MAX` (its setting CREW CHATTER: FULL 6, REDUCED 3, MINIMAL 1; not published or 0 means 6) and the program never picks an option number above it (`Pick`: the number of options of the message, or of the technician, is cut to that value before the choice; a coherent group keeps its number, cut to the same limit). Because the professional wording has the low numbers and a futile message starts with two silent options, REDUCED lets a futile line through only at its default and MINIMAL says the default wording everywhere and keeps the futile lines silent. The variable is part of the context the mission publishes only when it changes (`voice context publish`).

The pack is edited from `tools/variants-EN/*.json` with `tools/build-variants.js`; `tools/export-xlsx.js` writes the voice messages spreadsheet from it.

Tips of the crew (pack EN 13, the topic CND since EN 16): 48 messages `TIP.<topic>.1` to `4` (twelve topics, groups `tip<topic>`), spoken by the copilot, the doctor and sometimes the pilot, about functions of the mission; they are not futile (six options, none silent) because the mission decides with its own setting CREW TIPS, not with the chatter. The source texts are in `tools/variants-EN/tips.json` (the file was used to add the messages to the XML; the XML is the source of the pack).
